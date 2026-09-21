using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BpmMeasurer.Controls;

namespace BpmMeasurer;

public partial class MainWindow : Window
{
    private BpmAudioData? _audioData;
    private volatile int _bgmStream;
    private volatile int _decodeStream;
    private volatile bool _isPlaying;
    private volatile bool _isLoading;

    private readonly Stopwatch _frameClock = Stopwatch.StartNew();

    // FPS tracking
    private int _fpsFrameCount;
    private double _lastFpsUpdateTime;
    private double _currentFps;

    // Cache
    private WaveformEnvelope? _waveEnvelope;
    private SpectrogramData? _specCache;

    // Spectrogram tile set (built once on load, GPU-composited thereafter)
    private SpectrogramTileSet? _specTileSet;

    // Viewport
    private double _viewCenterTime;
    private double _viewHalfWidth;
    private bool _plotsConfigured;
    private bool _specConfigured;

    // Waveform tile set (built once on load, GPU-composited thereafter)
    private WaveformTileSet? _waveTileSet;

    // Timing state
    private double _globalOffset = 0.0;
    private List<RawTimingPoint> _rawPoints = new() { new RawTimingPoint(Guid.NewGuid(), 0, 120) };
    private IReadOnlyList<TimingPoint> _timingPoints = Array.Empty<TimingPoint>();

    // Currently-expanded segment row (null = all collapsed). Pure view state — not part of
    // undo snapshots, so Undo/Redo restores data without touching which row is open.
    private Guid? _expandedSegmentId;

    // One-shot flag set by auto-expand paths (drag / undo-redo / add-segment) so the next
    // RebuildSegmentList pins the expanded segment's bottom to the viewport bottom instead
    // of restoring the prior scroll offset. Consumed (reset to false) inside RebuildSegmentList.
    private bool _scrollExpandedToBottom;

    // Overlay canvas elements (dynamically managed)
    private readonly List<UIElement> _overlayElements = new();
    private readonly List<UIElement> _beatRowElements = new();

    // Drag state
    private enum DragMode { None, Seek, Offset, Bpm }
    private DragMode _dragMode;
    private double _dragStartX;
    private double _dragStartTime;
    private double _dragStartOffset;
    private double _dragBeatTarget;
    private double _dragTargetSegBeat;
    private SolidColorBrush? _dragDisplayColor;

    // Focus region state (independent of WPF keyboard focus)
    // _plotAreaHasFocus: VizGrid currently holds focus; _sidebarHasFocus: SidebarPanel currently holds focus.
    // _focusJustTransferred: marks the first gesture entering a region — suppresses click actions
    // (no seek / no offset / no BPM change) until a >3px drag confirms intent.
    private bool _plotAreaHasFocus;
    private bool _sidebarHasFocus;
    private bool _focusJustTransferred;

    private static readonly SolidColorBrush FocusHighlightBrush =
        new(Color.FromRgb(0x81, 0x8C, 0xF8));

    /// <summary>
    /// Toggles the overlay highlight borders around VizGrid and SidebarPanel based on the
    /// current focus-region state. BorderThickness stays constant (2px) — only the brush
    /// switches between transparent and the indigo highlight, so layout never shifts.
    /// </summary>
    private void UpdateFocusHighlights()
    {
        if (PlotAreaHighlight != null)
            PlotAreaHighlight.BorderBrush = _plotAreaHasFocus ? FocusHighlightBrush : Brushes.Transparent;
        if (SidebarHighlight != null)
            SidebarHighlight.BorderBrush = _sidebarHasFocus ? FocusHighlightBrush : Brushes.Transparent;
    }

    public MainWindow()
    {
        InitializeComponent();
        ApplyLocalizedTexts();

        AllowDrop = true;
        // 拖放不按扩展名过滤:任意文件都可放入。能否成功解码取决于启动时已注册的
        // BASS 插件集(bass_aac/bassflac/bassopus/basswebm + 内置),与文件选择器列出的
        // 扩展名一致;不支持或解析失败的文件会在 BpmAudioLoader.Load 中静默返回 null。
        // 一次拖入多个文件时,由 DroppedFiles 挑出一个音频与一个 .txt 配置(见 HandleDroppedFiles)。
        DragEnter += (s, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
        };
        Drop += (s, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                e.Handled = true;
                var (audio, config) = DroppedFiles.Resolve(files);
                Dispatcher.BeginInvoke(() => HandleDroppedFiles(audio, config));
            }
        };

        // ── InputBindings: Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z via Command system ──
        InputBindings.Add(new KeyBinding(UndoCommand, Key.Z, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(RedoCommand, Key.Y, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(RedoCommand, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        CommandBindings.Add(new CommandBinding(UndoCommand, UndoCommand_Executed));
        CommandBindings.Add(new CommandBinding(RedoCommand, RedoCommand_Executed));

        // ── PreviewKeyDown: Space play/pause only ──
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Space)
            {
                if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
                    return;
                e.Handled = true;
                if (_isPlaying) PausePlayback();
                else StartPlayback();
            }
        };

        OffsetStepper.Configure(
            new[] { 1.0, 0.1, 0.01 },
            double.NegativeInfinity, double.PositiveInfinity, 3,
            Color.FromRgb(0x4A, 0xDE, 0x80), false);
        OffsetStepper.SetValue(_globalOffset);
        OffsetStepper.ValueChanged += (s, v) =>
        {
            if (_isPlaying) PausePlayback();
            _globalOffset = v;
            RefreshTimingPoints();
            RecordTimingIfChanged();
        };

        InitUndo();
    }
}
