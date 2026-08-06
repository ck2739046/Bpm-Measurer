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

    private void ExportConfigBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_audioData == null) return;

        var dlg = new SaveFileDialog
        {
            Filter = "Timing config (*.txt)|*.txt",
            FileName = "timing_config.txt"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            double duration = _audioData?.Duration ?? double.MaxValue;
            var text = TimingConfigSerializer.Serialize(_globalOffset, _timingPoints, duration);
            File.WriteAllText(dlg.FileName, text);

            // 嵌入模式:写 manifest 告知宿主(HachimiDX)导出的配置路径与所用音频路径。
            if (App.StartupNotifyPath is not null)
            {
                var manifest = new
                {
                    config_path = dlg.FileName,
                    audio_path = _audioData!.FilePath ?? ""
                };
                var json = System.Text.Json.JsonSerializer.Serialize(manifest);
                File.WriteAllText(App.StartupNotifyPath, json);
                App.EmbeddedExported = true;
            }
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
        }
    }

    private void ImportConfigBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Timing config (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var text = File.ReadAllText(dlg.FileName);

            if (!TimingConfigParser.TryParse(text, out double offset, out List<RawTimingPoint> points, out string? error))
            {
                MessageBox.Show($"{Loc("ConfigImport_Failed")}\n{error}",
                    Loc("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Apply.
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
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc("ConfigImport_Failed")}\n{ex.Message}",
                Loc("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
