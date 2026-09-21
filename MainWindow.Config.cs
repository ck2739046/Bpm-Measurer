using System.IO;
using System.Reflection;
using Microsoft.Win32;
using System.Windows;
using WPFLocalizeExtension.Extensions;

namespace BpmMeasurer;

/// <summary>
/// Localization lookup, startup localized-text application, and the timing-config
/// Import / Export button handlers. Extracted from MainWindow as a partial.
/// Pure parse/serialize logic lives in <see cref="TimingConfigParser"/> /
/// <see cref="TimingConfigSerializer"/>.
/// </summary>
public partial class MainWindow
{
    public static string Loc(string key)
    {
        var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
        var fullKey = $"{assemblyName}:Langs:{key}";
        var locExtension = new LocExtension(fullKey);
        locExtension.ResolveLocalizedValue(out string? result);
        return result ?? key;
    }

    private void ApplyLocalizedTexts()
    {
        Title = Loc("WindowTitle");
        OpenBtnText.Text = Loc("ImportAudio");
        PlaceholderText.Text = Loc("DropHint");
        StopBtnText.Text = Loc("JumpToStart");
        MetronomeText.Text = Loc("Metronome");
        UpdateSpectrumModeText();
        PlayPauseText.Text = Loc("Play");
        FileNameText.Text = Loc("NoAudio");
        ImportConfigText.Text = Loc("ImportConfig_Btn");
        ExportConfigText.Text = Loc("ExportConfig_Btn");
        SegmentsHeader.Text = Loc("Segments_Title");
        AddSegmentText.Text = Loc("AddSegment_Btn");
    }

    // ── Import / Export config (plain-text format) ──
    // Line 1: global_offset = <seconds>
    // Line 2+: beat_index = <number>, bpm = <float>, beats_per_bar = <int>
    //         (beats_per_bar is optional on import; defaults to 4, clamped 1–20)

    /// <summary>
    /// 拖放时随音频一起带来的配置文件路径。音频加载会把 timing 状态重置为默认,
    /// 故交由 <see cref="LoadAudioFile"/> 在重置之后再套用;仅在两次拖放之间短暂存活。
    /// </summary>
    private string? _pendingConfigPath;

    /// <summary>
    /// 拖放落点分发。无音频可加载时,随行拖入的 .txt 静默忽略;
    /// 有音频时先暂存配置,待音频加载完成后再套用(否则会被加载末尾的状态重置覆盖)。
    /// </summary>
    private void HandleDroppedFiles(string? audioPath, string? configPath)
    {
        if (_isLoading)
        {
            DebugLog.Log($"Drop ignored while loading: audio={audioPath ?? "null"} config={configPath ?? "null"}");
            return;
        }

        if (audioPath is null)
        {
            if (configPath is null || _audioData is null) return;
            TryImportConfigFile(configPath);
            return;
        }

        _pendingConfigPath = configPath;
        LoadAudioFile(audioPath);
    }

    /// <summary>
    /// 当前 timing 状态是否已偏离刚加载音频时的默认值(offset=0,单个 beat_index=0/bpm=120 段)。
    /// 导入配置前据此决定是否询问导出。比较忽略 Id(每次加载都是新 Guid)。
    /// </summary>
    private bool HasCurrentConfig()
    {
        if (_audioData == null) return false;
        if (_globalOffset != 0.0) return true;
        if (_rawPoints.Count != 1) return true;
        var p = _rawPoints[0];
        return p.BeatIndex != 0 || p.Bpm != 120 || p.BeatsPerBar != 4;
    }

    private void ExportConfigBtn_Click(object sender, RoutedEventArgs e) => ExportCurrentConfig();

    private void ImportConfigBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Timing config (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        TryImportConfigFile(dlg.FileName);
    }

    /// <summary>读取并导入配置文件;读盘失败与解析失败均提示后返回 false。</summary>
    private bool TryImportConfigFile(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc("ConfigImport_Failed")}\n{ex.Message}",
                Loc("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        return TryImportConfigText(text);
    }

    /// <summary>
    /// 解析并导入配置文本。若当前已有非默认配置,先询问是否导出。
    /// 返回 false 表示未导入(解析失败,或导出未完成而中断)。
    /// </summary>
    private bool TryImportConfigText(string text)
    {
        if (!TimingConfigParser.TryParse(text, out double offset, out List<RawTimingPoint> points, out string? error))
        {
            MessageBox.Show($"{Loc("ConfigImport_Failed")}\n{error}",
                Loc("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        // 先解析成功再询问:坏文件不该触发一次多余的导出。
        if (HasCurrentConfig())
        {
            var choice = MessageBox.Show(Loc("ConfigPrompt_Text"), Loc("ConfigPrompt_Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            // 导出未完成(在保存对话框取消,或写盘失败)→ 中断导入,保留当前配置。
            if (choice == MessageBoxResult.Yes && !ExportCurrentConfig())
                return false;
        }

        ApplyTimingConfig(offset, points);
        return true;
    }

    /// <summary>把解析结果套用到 timing 状态(含按音频时长的钳制与 1ms 取整)。</summary>
    private void ApplyTimingConfig(double offset, List<RawTimingPoint> points)
    {
        if (_audioData != null)
        {
            OffsetStepper.SetRange(-_audioData.Duration, _audioData.Duration);
            offset = Math.Clamp(offset, -_audioData.Duration, _audioData.Duration);
        }
        _globalOffset = Math.Round(offset * 1000.0) / 1000.0;
        _rawPoints = points;
        RefreshTimingPoints();
        ResetUndoHistory();
        ResetExpandedSegmentToAnchor(); // open on the beat-0 anchor after import
    }

    /// <summary>
    /// 写出当前配置。返回 true 仅当文件确已写入磁盘;用户在保存对话框取消、或写盘失败均返回 false。
    /// 嵌入模式下同时刷新 manifest,使其始终指向最后一次成功导出的配置。
    /// </summary>
    private bool ExportCurrentConfig()
    {
        if (_audioData == null) return false;

        var dlg = new SaveFileDialog
        {
            Filter = "Timing config (*.txt)|*.txt",
            FileName = "timing_config.txt"
        };
        if (dlg.ShowDialog() != true) return false;

        try
        {
            var text = TimingConfigSerializer.Serialize(_globalOffset, _timingPoints, _audioData.Duration);
            File.WriteAllText(dlg.FileName, text);

            // 嵌入模式:写 manifest 告知宿主(HachimiDX)导出的配置路径与所用音频路径。
            // 每次导出整体覆盖,故 manifest 反映最后一次成功导出。
            if (App.StartupNotifyPath is not null)
            {
                var manifest = new
                {
                    config_path = dlg.FileName,
                    audio_path = _audioData.FilePath ?? ""
                };
                var json = System.Text.Json.JsonSerializer.Serialize(manifest);
                File.WriteAllText(App.StartupNotifyPath, json);
                App.EmbeddedExported = true;
            }
            return true;
        }
        catch (Exception ex)
        {
            // 嵌入模式:写盘失败以退出码 2 告知宿主。
            if (App.StartupNotifyPath is not null)
            {
                Environment.Exit(2);
            }
            MessageBox.Show($"{Loc("ConfigExport_Failed")}\n{ex.Message}",
                Loc("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
