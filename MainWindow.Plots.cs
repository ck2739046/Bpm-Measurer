using System.Windows;
using System.Windows.Media;

namespace BpmMeasurer;

/// <summary>
/// Canvas/bitmap configuration and the per-frame wave/spectrogram transforms
/// (scale + translate). Extracted from MainWindow as a partial.
/// Bitmaps are generated once in EnsurePlotsConfigured; thereafter only the
/// transform is updated each frame for GPU compositing.
/// </summary>
public partial class MainWindow
{
    private SpectrogramDisplayMode _spectrogramDisplayMode = SpectrogramDisplayMode.Bass;

    private void SetBothXLimits(double left, double right)
    {
        _viewHalfWidth = (right - left) / 2;
    }

    private void UpdateSpectrumModeText()
    {
        SpectrumModeText.Text = Loc(_spectrogramDisplayMode == SpectrogramDisplayMode.Bass
            ? "SpectrumModeBass"
            : "SpectrumModeNormal");
        SpectrumModeBtn.Background = new SolidColorBrush(
            _spectrogramDisplayMode == SpectrogramDisplayMode.Bass
                ? Color.FromRgb(0x25, 0xA3, 0xB3)   // Bass：青蓝
                : Color.FromRgb(0xCB, 0x41, 0x24)); // Normal：橘红
    }

    private void RebuildSpectrogramTiles()
    {
        _specTileSet?.Dispose();
        _specTileSet = null;

        if (_specCache == null)
        {
            _specConfigured = false;
            return;
        }

        _specTileSet = new SpectrogramTileSet(
            _specCache, SpectrogramCanvas, _spectrogramDisplayMode);
        _specTileSet.Build();
        _specConfigured = true;

        SpectrogramCanvas.Visibility = Visibility.Visible;
        SpectrogramCanvas.UpdateLayout();
        UpdateSpectrogramTransform();
    }

    private void SpectrumModeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
            PausePlayback();

        _spectrogramDisplayMode = _spectrogramDisplayMode == SpectrogramDisplayMode.Bass
            ? SpectrogramDisplayMode.Normal
            : SpectrogramDisplayMode.Bass;
        UpdateSpectrumModeText();

        if (_audioData == null || _specCache == null) return;
        RebuildSpectrogramTiles();
        RenderVisuals();
    }

    private void EnsurePlotsConfigured()
    {
        if (_audioData == null || _waveEnvelope == null || _specCache == null) return;

        if (!_plotsConfigured)
        {
            // Initial X range first
            SetBothXLimits(0, _audioData.Duration);
            _plotsConfigured = true;

            // Build waveform tiles (one WriteableBitmap per TileWidth columns) and add their
            // Images to the canvas. Keeps every GPU texture within hardware limits, avoiding
            // the MIL internal tiling that crashed the render thread on large single bitmaps.
            _waveTileSet = new WaveformTileSet(_waveEnvelope, WaveformCanvas);
            _waveTileSet.Build();

            WaveformCanvas.Visibility = Visibility.Visible;
            WaveformCanvas.UpdateLayout();
        }

        if (!_specConfigured)
            RebuildSpectrogramTiles();
    }

    private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_plotsConfigured)
            UpdateWaveformTransform();
    }

    private void SpectrogramCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_specConfigured)
            UpdateSpectrogramTransform();
    }

    // ── Rendering ──

    private void UpdateWaveformTransform()
    {
        if (_waveTileSet == null) return;
        double canvasW = WaveformCanvas.ActualWidth;
        double canvasH = WaveformCanvas.ActualHeight;
        if (canvasW <= 0) return;

        // NaN/Infinity/extreme-scale guards live inside TileSet.UpdateTransform, which
        // skips pushing bad matrices to the render thread (the original crash vector).
        _waveTileSet.UpdateTransform(_viewCenterTime, _viewHalfWidth, canvasW, canvasH);
    }

    private void UpdateSpectrogramTransform()
    {
        if (_specTileSet == null) return;
        double canvasW = SpectrogramCanvas.ActualWidth;
        double canvasH = SpectrogramCanvas.ActualHeight;
        if (canvasW <= 0) return;

        _specTileSet.UpdateTransform(_viewCenterTime, _viewHalfWidth, canvasW, canvasH);
    }
}
