using System.Windows.Controls;

namespace BpmMeasurer;

/// <summary>
/// Tiled spectrogram renderer. Splits a <see cref="SpectrogramData"/> into fixed-width
/// <see cref="WriteableBitmap"/> tiles. A single global <see cref="Range"/> (computed once)
/// is passed to every tile so brightness stays consistent across tile boundaries.
/// See <see cref="TileSet"/> for the lifecycle and transform model.
/// </summary>
internal sealed class SpectrogramTileSet : TileSet
{
    private readonly SpectrogramData _data;
    private readonly SpectrogramDisplayMode _mode;

    public SpectrogramTileSet(
        SpectrogramData data,
        Panel container,
        SpectrogramDisplayMode mode) : base(container)
    {
        _data = data;
        _mode = mode;
        // GlobalRange is precomputed on the background thread during
        // PrecomputedAudioData.ComputeSpectrogram, so every tile shares identical brightness
        // without rescanning the whole magnitude matrix here on the UI thread.
    }

    public override double PixelsPerSecond => _data.TimeStep > 0 ? 1.0 / _data.TimeStep : _data.Columns / _data.Duration;
    public override int BitmapPixelHeight => _data.FreqBands;

    public void Build()
    {
        // Guard against degenerate audio (Duration<=0 or no columns): avoids Infinity
        // PixelsPerSecond and a zero-length bitmap. UpdateTransform's NaN guard would
        // already swallow the transform, but bailing here skips needless allocations.
        if (_data.Duration <= 0 || _data.Columns <= 0) return;

        int totalCols = _data.Columns;
        int fullTiles = (totalCols + TileWidth - 1) / TileWidth;
        // Real per-column time step (hopSamples/SampleRate) anchors each tile to actual PCM
        // positions, eliminating the cumulative drift when sampleRate*0.005 is not an integer.
        double timePerCol = _data.TimeStep;
        // Shift each tile's origin so a column's on-screen center lands at its true energy
        // center (column start + half FFT window). WindowCenterOffset is the half-window phase
        // (up to ~186 ms at 22050 Hz); -0.5*timePerCol accounts for the tile origin being a
        // column's left edge. Without this the spectrogram leads the waveform/playhead.
        double centerShift = _data.WindowCenterOffset - 0.5 * timePerCol;

        for (int i = 0; i < fullTiles; i++)
        {
            int colStart = i * TileWidth;
            int colCount = System.Math.Min(TileWidth, totalCols - colStart);
            if (colCount <= 0) break;

            var bmp = SpectrogramBitmapRenderer.CreateTile(
                _data, colStart, colCount, _data.GlobalRange, _mode);
            var image = CreateTileImage(bmp);
            var (scale, translate) = GetTransforms(image);

            _tiles.Add(new Tile
            {
                Bitmap = bmp,
                Image = image,
                Scale = scale,
                Translate = translate,
                TimeStart = colStart * timePerCol + centerShift,
                ColStart = colStart,
                ColCount = colCount,
            });
            _container.Children.Add(image);
        }
    }
}
