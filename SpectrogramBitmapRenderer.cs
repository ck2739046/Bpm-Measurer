using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BpmMeasurer;

public enum SpectrogramDisplayMode
{
    Bass,
    Normal
}

/// <summary>Converts spectrogram data using the selected display mode.</summary>
public static class SpectrogramBitmapRenderer
{
    private const double NormalFrequencyBoostStartHz = 300.0;
    private const double NormalFrequencyBoostFullHz = 3000.0;
    private const double NormalFrequencyBrightnessBoost = 0.20;

    public static WriteableBitmap Create(
        SpectrogramData data,
        SpectrogramDisplayMode mode = SpectrogramDisplayMode.Bass)
        => CreateTile(data, 0, data.Columns, ComputeGlobalRange(data.Magnitudes), mode);

    /// <summary>
    /// Parallel chunked min/max over raw magnitudes. Local per-thread reduction
    /// followed by a sequential global merge (O(threads), negligible). Public so
    /// tile-based callers can compute the global range once and pass it to every
    /// <see cref="CreateTile"/> call, keeping brightness consistent across tiles.
    /// </summary>
    public static Range ComputeGlobalRange(float[,] mags)
        => ComputeRangeParallel(mags);

    /// <summary>
    /// Renders a horizontal slice of the spectrogram (columns
    /// [colStart, colStart+colCount)) into a WriteableBitmap of width
    /// <paramref name="colCount"/>. The caller-supplied <paramref name="globalRange"/>
    /// ensures consistent brightness across tiles.
    /// </summary>
    public static WriteableBitmap CreateTile(
        SpectrogramData data,
        int colStart,
        int colCount,
        Range globalRange,
        SpectrogramDisplayMode mode)
    {
        int w = colCount;
        int h = data.FreqBands;
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);

        var lut = mode == SpectrogramDisplayMode.Normal
            ? AuditionLikeSpectrogramColormap.Lut
            : WaveSpectrogramColormap.Lut;

        // Y-axis exponential remap (merged with Y-flip into the pixel fill loop),
        // avoiding a separate resampled array.
        double yExp = mode == SpectrogramDisplayMode.Normal ? 1.0 : 1.8;
        int maxSrcIndex = h - 1;

        var mags = data.Magnitudes;
        var range = globalRange;

        // ── Phase 2: parallel pixel fill using the computed range ──
        var pixels = new int[w * h];
        Parallel.For(0, h, PrecomputeParallel.Options, y =>
        {
            double visualNorm = (h - 1.0 - y) / (double)maxSrcIndex;
            double srcBandFloat = Math.Pow(visualNorm, yExp) * maxSrcIndex;
            int srcLo = (int)srcBandFloat;
            int srcHi = Math.Min(srcLo + 1, maxSrcIndex);
            float frac = (float)(srcBandFloat - srcLo);
            float oneMinusFrac = 1f - frac;
            double brightnessScale = 1.0;
            if (mode == SpectrogramDisplayMode.Normal && data.SampleRate > 0)
            {
                double bandNorm = srcBandFloat / maxSrcIndex;
                double logBase = data.FrequencyLogBase;
                double binNorm = logBase > 1.0
                    ? (Math.Pow(logBase, bandNorm) - 1.0) / (logBase - 1.0)
                    : bandNorm;
                double frequencyHz = binNorm * data.SampleRate * 0.5;
                double boostPosition = Math.Clamp(
                    (frequencyHz - NormalFrequencyBoostStartHz) /
                    (NormalFrequencyBoostFullHz - NormalFrequencyBoostStartHz),
                    0.0, 1.0);
                double smoothBoost = boostPosition * boostPosition * (3.0 - 2.0 * boostPosition);
                brightnessScale += NormalFrequencyBrightnessBoost * smoothBoost;
            }

            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                int srcCol = colStart + x;
                float mag = mags[srcLo, srcCol] * oneMinusFrac
                          + mags[srcHi, srcCol] * frac;
                double fraction = range.Normalize(mag, true);
                fraction = Math.Min(1.0, fraction * brightnessScale);
                pixels[rowOffset + x] = lut[(int)(fraction * 255)];
            }
        });

        // ── Phase 3: single blit to GPU back buffer ──
        bmp.Lock();
        try
        {
            Marshal.Copy(pixels, 0, bmp.BackBuffer, pixels.Length);
            bmp.AddDirtyRect(new Int32Rect(0, 0, w, h));
        }
        finally
        {
            bmp.Unlock();
        }

        return bmp;
    }

    /// <summary>
    /// Parallel chunked min/max over raw magnitudes. Local per-thread reduction
    /// followed by a sequential global merge (O(threads), negligible).
    /// </summary>
    private static Range ComputeRangeParallel(float[,] mags)
    {
        int h = mags.GetLength(0);
        int w = mags.GetLength(1);
        int parallelism = PrecomputeParallel.Options.MaxDegreeOfParallelism;
        var localRange = new (double Min, double Max)[parallelism];
        int chunkRows = (h + parallelism - 1) / parallelism;

        Parallel.For(0, parallelism, PrecomputeParallel.Options, tid =>
        {
            int yStart = tid * chunkRows;
            int yEnd = Math.Min(yStart + chunkRows, h);
            if (yStart >= yEnd) { localRange[tid] = (0, 0); return; }

            double locMin = double.MaxValue;
            double locMax = double.MinValue;
            for (int y = yStart; y < yEnd; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float v = mags[y, x];
                    if (v < locMin) locMin = v;
                    if (v > locMax) locMax = v;
                }
            }
            localRange[tid] = (locMin, locMax);
        });

        double globalMin = localRange[0].Min;
        double globalMax = localRange[0].Max;
        for (int i = 1; i < parallelism; i++)
        {
            if (localRange[i].Min < globalMin) globalMin = localRange[i].Min;
            if (localRange[i].Max > globalMax) globalMax = localRange[i].Max;
        }
        return new Range(globalMin, globalMax);
    }
}
