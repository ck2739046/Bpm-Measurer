using Microsoft.Win32;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Un4seen.Bass;
using Un4seen.Bass.AddOn.Fx;

namespace BpmMeasurer;

/// <summary>
/// Audio playback (BASS) lifecycle, file loading, and the composition-frame
/// render driver. Extracted from MainWindow as a partial — shares all private
/// instance fields with MainWindow.xaml.cs.
/// </summary>
public partial class MainWindow
{
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_DEFAULT, handle);
        // 降低输出缓冲，提升 mixtime sync 触发的 click 与 BGM 的对齐精度
        // 顺序：先设 update period（影响 buffer 最小值 buffer>=update+1），再设 buffer
        Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_UPDATEPERIOD, 5);
        Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_BUFFER, 10);

        // 注册 BASS 插件(x64,须与 bass.dll 同位数):扩展 BASS_StreamCreateFile 可解码的
        // 容器/编码,须在 BASS_Init 之后调用。失败不致命(对应格式将无法解码),仅写 Debug 日志。
        //   bass_aac.dll  → AAC:  .aac(ADTS)、.m4a/.m4b/.mp4(MP4 容器)
        //   bassflac.dll  → FLAC: .flac(及 Ogg-FLAC .oga/.ogg)
        //   bassopus.dll  → Opus: .opus(Ogg/Opus;亦可 .oga/.ogg 内 Opus 轨)
        //   basswebm.dll  → WebM & Matroska: .webm/.mka/.mkv(音频轨可为 Opus/Vorbis)
        foreach (var plugin in new[] { "bass_aac.dll", "bassflac.dll", "bassopus.dll", "basswebm.dll" })
        {
            if (Bass.BASS_PluginLoad(plugin) == 0)
                System.Diagnostics.Debug.WriteLine($"BASS_PluginLoad({plugin}) failed: {Bass.BASS_ErrorGetCode()}");
        }

        // 启动时若指定了音频(--audio= 或位置参数),自动加载。
        // 延后一帧:确保 BASS_Init 已完成、UI 控件布局就绪,避免在 Loaded 同步栈中阻塞。
        var startupPath = App.StartupAudioPath;
        if (!string.IsNullOrEmpty(startupPath))
        {
            Dispatcher.BeginInvoke(new Action(() => LoadAudioFile(startupPath)));
        }
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRenderingFrame;
        FreeMetronomeClicks();
        StopAndFreeStreams();
        _waveTileSet?.Dispose();
        _specTileSet?.Dispose();
        Bass.BASS_Free();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_audioData != null)
            RenderVisuals();
    }

    private void OpenBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc("SelectAudioFile"),
            // 扩展名清单与各 BASS 组件对应(未命中下列扩展名、或用「所有文件」选的文件,
            // 拖放/导入后统一交 BASS_StreamCreateFile 解码,能否成功取决于已加载的插件):
            //   内置(bass.dll) : .mp3/.mp2/.mp1 .wav .ogg(Vorbis) .aiff/.aif
            //                    (另依赖系统 ACM/Media Foundation 解码:WMA 等)
            //   bass_aac.dll   : .aac .m4a/.m4b/.mp4
            //   bassflac.dll   : .flac .oga(Ogg-FLAC)
            //   bassopus.dll   : .opus(.oga/.ogg 内 Opus)
            //   basswebm.dll   : .mka/.mkv/.webm
            Filter = $"{Loc("AudioFiles")}|*.mp3;*.mp2;*.wav;*.ogg;*.oga;*.flac;*.aac;*.m4a;*.m4b;*.mp4;*.opus;*.mka;*.mkv;*.webm;*.aiff;*.aif|{Loc("AllFiles")}|*.*"
        };
        if (dlg.ShowDialog() == true)
            LoadAudioFile(dlg.FileName);
    }

    private void PlayPauseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying) PausePlayback();
        else StartPlayback();
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        JumpToStart();
    }

    // ── Playback ──

    private void StopAndFreeStreams()
    {ClearMetronomeSyncs();
        
        if (_bgmStream != 0)
        {
            Bass.BASS_ChannelStop(_bgmStream);
            // BASS_FX_FREESOURCE auto-frees the decode stream,
            // so clear the handle to avoid double-free below.
            Bass.BASS_StreamFree(_bgmStream);
            _bgmStream = 0;
            _decodeStream = 0;
        }
        else if (_decodeStream != 0)
        {
            Bass.BASS_StreamFree(_decodeStream);
            _decodeStream = 0;
        }
        _isPlaying = false;
    }

    private async void LoadAudioFile(string filePath)
    {
        if (_isLoading) return;
        _isLoading = true;
        OpenBtn.IsEnabled = false;

        StopAndFreeStreams();
        CompositionTarget.Rendering -= OnRenderingFrame;

        // Clear old visual state before loading new file
        WaveformCanvas.Visibility = Visibility.Collapsed;
        SpectrogramCanvas.Visibility = Visibility.Collapsed;
        SampleRateText.Text = "-";
        DurationText.Text = "-";

        LoadTimingLogger.Begin(filePath);

        var audioData = await Task.Run(() => BpmAudioLoader.Load(filePath));
        LoadTimingLogger.Phase("Audio decode");

        if (audioData == null)
        {
            LoadTimingLogger.End("Decode failed");
            _isLoading = false;
            OpenBtn.IsEnabled = true;
            PlaceholderText.Visibility = Visibility.Visible;
            FileNameText.Text = Loc("NoAudio");
            MessageBox.Show(Loc("LoadError"), Loc("Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _audioData = audioData;

        _decodeStream = Bass.BASS_StreamCreateFile(filePath, 0L, 0L, BASSFlag.BASS_STREAM_DECODE);
        if (_decodeStream == 0)
        {
            LoadTimingLogger.End("BASS decode stream failed");
            _audioData = null;
            _isLoading = false;
            OpenBtn.IsEnabled = true;
            PlaceholderText.Visibility = Visibility.Visible;
            FileNameText.Text = Loc("NoAudio");
            MessageBox.Show(Loc("LoadError"), Loc("Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _bgmStream = BassFx.BASS_FX_TempoCreate(_decodeStream, BASSFlag.BASS_FX_FREESOURCE);
        if (_bgmStream == 0)
        {
            LoadTimingLogger.End("BASS tempo stream failed");
            Bass.BASS_StreamFree(_decodeStream);
            _decodeStream = 0;
            _audioData = null;
            _isLoading = false;
            OpenBtn.IsEnabled = true;
            PlaceholderText.Visibility = Visibility.Visible;
            FileNameText.Text = Loc("NoAudio");
            MessageBox.Show(Loc("LoadError"), Loc("Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        LoadTimingLogger.Phase("BASS stream create");

        // 节拍器 click 采样须在 BASS_Init 后创建。
        EnsureMetronomeClicks();
        UpdateMetronomeMix();

        // Show loading indicator
        PlaceholderText.Visibility = Visibility.Collapsed;
        LoadingText.Visibility = Visibility.Visible;
        LoadingText.Text = Loc("LoadingAudio");

        await Task.Run(() =>
        {
            _waveEnvelope = PrecomputedAudioData.ComputeWaveform(
                _audioData.RawSamples, _audioData.Duration, _audioData.SampleRate);
        });
        LoadTimingLogger.Phase("Waveform precompute");
        _audioData.RawSamples = null!; // Free ~50MB+ for long audio, no longer needed

        await Task.Run(() =>
        {
            _specCache = PrecomputedAudioData.ComputeSpectrogram(
                _audioData.FilePath, _audioData.Duration);
        });
        LoadTimingLogger.Phase("Spectrogram precompute");

        LoadingText.Visibility = Visibility.Collapsed;

        _viewCenterTime = 0;
        _plotsConfigured = false;
        _specConfigured = false;
        // Tear down old tiles (removes their Images from the canvases and drops the
        // WriteableBitmaps) before building fresh ones in EnsurePlotsConfigured.
        _waveTileSet?.Dispose();
        _waveTileSet = null;
        _specTileSet?.Dispose();
        _specTileSet = null;

        FileNameText.Text = System.IO.Path.GetFileName(filePath);
        SampleRateText.Text = $"{_audioData.SampleRate} Hz";
        DurationText.Text = $"{_audioData.Duration:F2}s";
        TimeText.Text = "0.000s";
        FpsText.Text = "FPS: -";

        // Reset FPS tracking
        _fpsFrameCount = 0;
        _lastFpsUpdateTime = _frameClock.Elapsed.TotalSeconds;

        PlayPauseBtn.IsEnabled = true;
        StopBtn.IsEnabled = true;
        MetronomeBtn.IsEnabled = true;
        PlayPauseEmoji.Text = "▶️";
        PlayPauseText.Text = Loc("Play");

        _isLoading = false;
        OpenBtn.IsEnabled = true;

        RenderVisuals();
        LoadTimingLogger.Phase("Render visuals");

        // Initialize timing state
        _globalOffset = 0.0;
        _rawPoints = new List<RawTimingPoint> { new RawTimingPoint(Guid.NewGuid(), 0, 120) };
        OffsetStepper.SetRange(-_audioData.Duration, _audioData.Duration);
        RefreshTimingPoints();
        ResetUndoHistory();
        ResetExpandedSegmentToAnchor(); // open on the beat-0 anchor after a fresh load
        SidebarPanel.Visibility = Visibility.Visible;
        OverlayCanvas.Visibility = Visibility.Visible;
        BeatRowCanvas.Visibility = Visibility.Visible;

        LoadTimingLogger.End($"Duration={_audioData.Duration:F2}s  SR={_audioData.SampleRate}Hz  Ch={_audioData.Channels}");
    }

    private void StartPlayback()
    {
        if (_bgmStream == 0) return;

        var active = Bass.BASS_ChannelIsActive(_bgmStream);

        if (active == BASSActive.BASS_ACTIVE_PAUSED)
        {
            Bass.BASS_ChannelPlay(_bgmStream, false);
        }
        else
        {
            var pos = Bass.BASS_ChannelGetPosition(_bgmStream);
            var time = Bass.BASS_ChannelBytes2Seconds(_bgmStream, pos);
            if (_audioData != null && time >= _audioData.Duration - 0.1)
                Bass.BASS_ChannelSetPosition(_bgmStream, 0);

            Bass.BASS_ChannelPlay(_bgmStream, false);
        }

        _isPlaying = true;
        CompositionTarget.Rendering += OnRenderingFrame;
        PlayPauseEmoji.Text = "⏸️";

        if (_metronomeEnabled)
        {
            var pos = Bass.BASS_ChannelGetPosition(_bgmStream);
            ArmMetronome(Bass.BASS_ChannelBytes2Seconds(_bgmStream, pos));
        }
        ClearMetronomeSyncs();
        PlayPauseText.Text = Loc("Pause");
    }

    private void PausePlayback()
    {
        CompositionTarget.Rendering -= OnRenderingFrame;
        if (_bgmStream != 0)
            Bass.BASS_ChannelPause(_bgmStream);
        _isPlaying = false;
        PlayPauseEmoji.Text = "▶️";
        PlayPauseText.Text = Loc("Play");
        FpsText.Text = "FPS: -";
    }

    private void JumpToStart()
    {
        if (_bgmStream == 0) return;

        CompositionTarget.Rendering -= OnRenderingFrame;
        ClearMetronomeSyncs();
        if (_isPlaying)
        {
            Bass.BASS_ChannelPause(_bgmStream);
            _isPlaying = false;
            PlayPauseEmoji.Text = "▶️";
            PlayPauseText.Text = Loc("Play");
        }

        Bass.BASS_ChannelSetPosition(_bgmStream, 0);
        _viewCenterTime = 0;
        TimeText.Text = "0.000s";
        RenderVisuals();
    }

    // ── Frame rendering: driven by WPF composition thread ──

    private void OnRenderingFrame(object? sender, EventArgs e)
    {
        if (!_isPlaying || _bgmStream == 0) return;

        var pos = Bass.BASS_ChannelGetPosition(_bgmStream);
        var time = Bass.BASS_ChannelBytes2Seconds(_bgmStream, pos);
        _viewCenterTime = time;

        // Time text update — already on UI thread, no dispatcher overhead
        TimeText.Text = $"{time:F3}s";

        RenderVisuals();
        RefillMetronomeIfNeeded(time);
    }

    // ── Playback seeking ──

    private void SeekBassTo(double seconds)
    {
        if (_bgmStream == 0) return;
        var bytePos = Bass.BASS_ChannelSeconds2Bytes(_bgmStream, seconds);
        Bass.BASS_ChannelSetPosition(_bgmStream, bytePos);
    }

    private double _metronomeVolume = 0.5;
    private float _effectiveClickVolume = 0.7f;
    private const double MaxClickVolume = 1.4;
    private const double MinBgmVolume = 0.6;

    private void MetronomeVolumeSlider_ValueChanged(
        object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _metronomeVolume = Math.Clamp(e.NewValue / 100.0, 0.0, 1.0);
        MetronomeVolumeValueText.Text = $"{Math.Round(e.NewValue):0}%";
        UpdateMetronomeMix();
    }

    private void UpdateMetronomeMix()
    {
        float clickVolume = (float)(_metronomeVolume * MaxClickVolume);
        System.Threading.Volatile.Write(ref _effectiveClickVolume, clickVolume);
        ApplyEffectiveBgmVolume();
    }

    private void ApplyEffectiveBgmVolume()
    {
        if (_bgmStream == 0) return;
        double vol = _metronomeEnabled
            ? 1.0 - _metronomeVolume * (1.0 - MinBgmVolume)
            : 1.0;
        Bass.BASS_ChannelSetAttribute(_bgmStream, BASSAttribute.BASS_ATTRIB_VOL, (float)vol);
    }

    private void MetronomeBtn_Click(object sender, RoutedEventArgs e)
    {
        _metronomeEnabled = !_metronomeEnabled;
        MetronomeEmoji.Text = _metronomeEnabled ? "🔊" : "🔇";
        MetronomeBtn.Background = new SolidColorBrush(
            _metronomeEnabled ? Color.FromRgb(0x1E, 0x6B, 0x3A)   // 启用：暗绿
                              : Color.FromRgb(0x3A, 0x3A, 0x3A));   // 关闭：灰
        ApplyEffectiveBgmVolume();
        if (_isPlaying)
        {
            if (_metronomeEnabled)
            {
                EnsureMetronomeClicks();
                var pos = Bass.BASS_ChannelGetPosition(_bgmStream);
                ArmMetronome(Bass.BASS_ChannelBytes2Seconds(_bgmStream, pos));
            }
            else
            {
                ClearMetronomeSyncs();
            }
        }
    }
}
