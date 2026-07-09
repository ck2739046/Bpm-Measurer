using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BpmMeasurer;

/// <summary>
/// Per-frame render orchestration, OverlayCanvas ↔ time coordinate conversion,
/// and the beat grid / beat-row overlays. Extracted from MainWindow as a partial.
/// Beat-row triangle/number density delegates to <see cref="BeatGridMath.GetBarDensityInterval"/>
/// (per-segment, time-signature-anchored); vertical beat lines use <see cref="BeatGridMath.GetVertInterval"/>.
/// </summary>
public partial class MainWindow
{
    private void RenderVisuals()
    {
        if (_audioData == null || _waveEnvelope == null || _specCache == null) return;

        try
        {
            RenderVisualsCore();
        }
        catch (Exception ex)
        {
            DebugLog.Log($"RenderVisuals CRASH: viewHalfWidth={_viewHalfWidth}, viewCenterTime={_viewCenterTime}");
            // 仅记录上下文到缓冲区，不在此写 crashtxt；
            // rethrow 后由全局 DispatcherUnhandledException 统一落盘。
            DebugLog.Log($"RenderVisuals exception: {ex.GetType().FullName}: {ex.Message}");
            throw;
        }
    }

    private void RenderVisualsCore()
    {
        if (!_plotsConfigured || !_specConfigured)
            EnsurePlotsConfigured();

        // Waveform — only transform, no bitmap regeneration
        UpdateWaveformTransform();

        // Spectrogram — only transform, no bitmap regeneration
        UpdateSpectrogramTransform();

        // Overlay — beat grid lines + playhead
        RenderBeatGrid();
        RenderBeatRow();

        // ── FPS calculation ──
        if (_isPlaying)
        {
            _fpsFrameCount++;
            double now = _frameClock.Elapsed.TotalSeconds;
            double elapsed = now - _lastFpsUpdateTime;
            if (elapsed >= 0.3)
            {
                _currentFps = _fpsFrameCount / elapsed;
                _fpsFrameCount = 0;
                _lastFpsUpdateTime = now;
                FpsText.Text = $"FPS: {_currentFps:F0}";
            }
        }
        else
        {
            FpsText.Text = "FPS: -";
        }
    }

    // ── Coordinate conversion (OverlayCanvas pixel ↔ time) ──

    private double TimeToCanvasX(double time)
    {
        if (_audioData == null) return 0;
        double canvasW = OverlayCanvas.ActualWidth;
        if (canvasW <= 0) return 0;

        // Same transform as waveform: left edge = _viewCenterTime - _viewHalfWidth
        double leftTime = _viewCenterTime - _viewHalfWidth;
        double dataSpan = _viewHalfWidth * 2.0;
        return (time - leftTime) * canvasW / dataSpan;
    }

    private double CanvasXToTime(double x)
    {
        if (_audioData == null) return 0;
        double canvasW = OverlayCanvas.ActualWidth;
        if (canvasW <= 0) return 0;

        double leftTime = _viewCenterTime - _viewHalfWidth;
        double dataSpan = _viewHalfWidth * 2.0;
        return leftTime + x * dataSpan / canvasW;
    }

    // ── Beat Grid rendering on OverlayCanvas ──

    private void RenderBeatGrid()
    {
        // Clear previous elements
        foreach (var el in _overlayElements)
            OverlayCanvas.Children.Remove(el);
        _overlayElements.Clear();

        if (_audioData == null || _timingPoints.Count == 0) return;

        double canvasW = OverlayCanvas.ActualWidth;
        double canvasH = OverlayCanvas.ActualHeight;
        if (canvasW <= 0 || canvasH <= 0) return;

        double dataSpan = _viewHalfWidth * 2.0;

        double leftTime = _viewCenterTime - _viewHalfWidth;
        double rightTime = _viewCenterTime + _viewHalfWidth;

        for (int i = 0; i < _timingPoints.Count; i++)
        {
            var point = _timingPoints[i];
            var nextPoint = (i + 1 < _timingPoints.Count) ? _timingPoints[i + 1] : (TimingPoint?)null;

            double interval = 60.0 / point.Bpm;
            double pxPerBeat = canvasW / dataSpan * interval;
            int vertInterval = BeatGridMath.GetVertInterval(pxPerBeat);
            // Find the first beat visible
            double startTimeOffset = Math.Max(0, leftTime - point.Time);
            int startRelBeat = Math.Max(0, (int)Math.Ceiling(startTimeOffset / interval));

            int relBeat = startRelBeat;
            double waveH = WaveformCanvas.ActualHeight;
            double beatRowH = BeatRowCanvas.ActualHeight;
            double specTop = waveH + beatRowH;

            while (true)
            {
                // Stop at the next segment's start beat — old segment owns no beats past it.
                if (nextPoint.HasValue && point.BeatIndex + relBeat >= nextPoint.Value.BeatIndex) break;

                double beatTime = point.Time + relBeat * interval;
                if (beatTime > rightTime) break;
                if (relBeat > 0 && beatTime > _audioData.Duration) break;

                double x = TimeToCanvasX(beatTime);
                if (x < -50 || x > canvasW + 50) { relBeat++; continue; }

                bool isSectionStart = (relBeat == 0);
                bool isWholeBeat = Math.Abs(relBeat - Math.Round((double)relBeat)) < 0.001;

                // Skip beats based on density
                if (!isSectionStart && relBeat % vertInterval != 0) { relBeat++; continue; }

                if (isWholeBeat)
                {
                    var color = isSectionStart
                        ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80))
                        : Brushes.White;
                    double thickness = isSectionStart ? 2.0 : 1.0;

                    // Line in waveform area only (Y=0 to waveH)
                    var waveLine = new Line
                    {
                        X1 = x, Y1 = 0, X2 = x, Y2 = waveH,
                        Stroke = color, StrokeThickness = thickness
                    };
                    _overlayElements.Add(waveLine);
                    OverlayCanvas.Children.Add(waveLine);

                    // Line in spectrogram area only (Y=specTop to canvasH)
                    var specLine = new Line
                    {
                        X1 = x, Y1 = specTop, X2 = x, Y2 = canvasH,
                        Stroke = color, StrokeThickness = thickness
                    };
                    _overlayElements.Add(specLine);
                    OverlayCanvas.Children.Add(specLine);
                }

                relBeat++;
            }
        }

        // Playhead line (yellow, centered)
        double playheadX = TimeToCanvasX(_viewCenterTime);
        if (playheadX >= -2 && playheadX <= canvasW + 2)
        {
            var playheadLine = new Line
            {
                X1 = playheadX, Y1 = 0, X2 = playheadX, Y2 = canvasH,
                Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0x00)), StrokeThickness = 2
            };
            _overlayElements.Add(playheadLine);
            OverlayCanvas.Children.Add(playheadLine);
        }
    }

    // ── Beat Row rendering (numbers between waveform and spectrogram) ──

    private void RenderBeatRow()
    {
        foreach (var el in _beatRowElements)
            BeatRowCanvas.Children.Remove(el);
        _beatRowElements.Clear();

        if (_audioData == null || _timingPoints.Count == 0) return;

        double canvasW = OverlayCanvas.ActualWidth;
        if (canvasW <= 0) return;

        double dataSpan = _viewHalfWidth * 2.0;

        double leftTime = _viewCenterTime - _viewHalfWidth;
        double rightTime = _viewCenterTime + _viewHalfWidth;

        for (int i = 0; i < _timingPoints.Count; i++)
        {
            var point = _timingPoints[i];
            var nextPoint = (i + 1 < _timingPoints.Count) ? _timingPoints[i + 1] : (TimingPoint?)null;

            double interval = 60.0 / point.Bpm;
            double pxPerBeat = canvasW / dataSpan * interval;
            int densityInterval = BeatGridMath.GetBarDensityInterval(pxPerBeat, point.BeatsPerBar);
            double startTimeOffset = Math.Max(0, leftTime - point.Time);
            int startRelBeat = Math.Max(0, (int)Math.Ceiling(startTimeOffset / interval));

            int relBeat = startRelBeat;
            while (true)
            {
                // Stop at the next segment's start beat — old segment owns no beats past it.
                if (nextPoint.HasValue && point.BeatIndex + relBeat >= nextPoint.Value.BeatIndex) break;

                double beatTime = point.Time + relBeat * interval;
                if (beatTime > rightTime) break;
                if (beatTime > _audioData.Duration) break;

                double x = TimeToCanvasX(beatTime);
                if (x < -50 || x > canvasW + 50) { relBeat++; continue; }

                // Show triangle+number based on per-segment bar density, anchored at segment start.
                // Section-start (relBeat==0) is always shown even if the grid would hide it.
                bool isSectionStart = (relBeat == 0);
                bool showHere = isSectionStart || (relBeat % densityInterval == 0);

                if (showHere)
                {
                    double globalBeatIndex = point.BeatIndex + relBeat;
                    string beatLabel = Math.Abs(globalBeatIndex % 1.0) < 0.0001
                        ? ((long)Math.Round(globalBeatIndex)).ToString()
                        : globalBeatIndex.ToString("0.#");

                    var beatColor = isSectionStart
                        ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80))
                        : Brushes.White;
                    var displayColor = _dragDisplayColor ?? beatColor;

                    // Upper triangle ▲
                    var upTri = new TextBlock
                    {
                        Text = "▲",
                        Foreground = displayColor,
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center,
                        Width = 30
                    };
                    Canvas.SetLeft(upTri, x - upTri.Width / 2);
                    Canvas.SetTop(upTri, 0);
                    _beatRowElements.Add(upTri);
                    BeatRowCanvas.Children.Add(upTri);

                    // Beat number
                    var tb = new TextBlock
                    {
                        Text = beatLabel,
                        Foreground = displayColor,
                        FontSize = isSectionStart ? 14 : 12,
                        FontWeight = isSectionStart ? FontWeights.Bold : FontWeights.Normal,
                        FontFamily = new FontFamily("Consolas"),
                        TextAlignment = TextAlignment.Center,
                        Width = 50
                    };
                    Canvas.SetLeft(tb, x - tb.Width / 2);
                    Canvas.SetTop(tb, 12);
                    _beatRowElements.Add(tb);
                    BeatRowCanvas.Children.Add(tb);

                    // Lower triangle ▼
                    var downTri = new TextBlock
                    {
                        Text = "▼",
                        Foreground = displayColor,
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center,
                        Width = 30
                    };
                    Canvas.SetLeft(downTri, x - downTri.Width / 2);
                    Canvas.SetTop(downTri, 22);
                    _beatRowElements.Add(downTri);
                    BeatRowCanvas.Children.Add(downTri);
                }

                relBeat++;
            }
        }
    }
}
