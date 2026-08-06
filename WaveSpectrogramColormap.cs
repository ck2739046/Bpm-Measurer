namespace BpmMeasurer;

/// <summary>
/// 7-segment colormap: black → rich blue → turquoise → warm gold (peak) → vivid orange → deep red → purple.
/// Mid-range gold is the perceptual brightness peak. Regular low frequencies display as deep red.
/// Ultra-low frequencies break to purple for clear visual separation.
/// </summary>
public class WaveSpectrogramColormap : IColormap
{
    public string Name => "Wave Spectrogram";

    /// <summary>
    /// 256-entry lookup table mapping fraction [0,1] → ARGB int.
    /// Eliminates per-pixel GetColor() calls for ~3-5× speedup in the render hot path.
    /// </summary>
    public static readonly int[] Lut = BuildLut();

    private static int[] BuildLut()
    {
        var inst = new WaveSpectrogramColormap();
        var lut = new int[256];
        for (int i = 0; i < 256; i++)
            lut[i] = unchecked((int)inst.GetColor(i / 255.0).ARGB);
        return lut;
    }

    public ArgbColor GetColor(double fraction)
    {
        if (!double.IsFinite(fraction))
            return ArgbColor.FromHex("#000000");

        double t;
        int r, g, b;

        if (fraction < 0.17)
        {
            // black → deep navy
            t = fraction / 0.17;
            r = 0; g = 0; b = (int)(t * 50);
        }
        else if (fraction < 0.33)
        {
            // deep navy → rich blue
            t = (fraction - 0.17) / 0.16;
            r = 0; g = (int)(t * 30); b = 50 + (int)(t * 160);
        }
        else if (fraction < 0.50)
        {
            // rich blue → turquoise
            t = (fraction - 0.33) / 0.17;
            r = 0; g = 30 + (int)(t * 130); b = 210;
        }
        else if (fraction < 0.67)
        {
            // turquoise → warm gold  ★ perceptual brightness peak
            t = (fraction - 0.50) / 0.17;
            r = (int)(t * 255); g = 160 + (int)(t * 30); b = (int)(210 * (1 - t));
        }
        else if (fraction < 0.83)
        {
            // warm gold → vivid orange
            t = (fraction - 0.67) / 0.16;
            r = 255; g = 190 - (int)(t * 70); b = (int)(t * 40);
        }
        else if (fraction < 0.88)
        {
            // vivid orange → deep red  (regular low frequencies)
            t = (fraction - 0.83) / 0.05;
            r = 255; g = 120 - (int)(t * 100); b = 40 - (int)(t * 40);
        }
        else
        {
            // deep red → purple  (ultra-low frequencies)
            t = (fraction - 0.88) / 0.12;
            r = 255 - (int)(t * 75); g = (int)(20 * (1 - t)); b = (int)(t * 180);
        }

        return new ArgbColor((byte)r, (byte)g, (byte)b);
    }
}

public sealed class AuditionLikeSpectrogramColormap : IColormap
{
    private static readonly (double Position, ArgbColor Color)[] Stops =
    {
        (0.00, new ArgbColor(0, 0, 0)),
        (0.10, new ArgbColor(5, 5, 22)),
        (0.22, new ArgbColor(17, 16, 79)),
        (0.34, new ArgbColor(69, 16, 110)),
        (0.48, new ArgbColor(143, 24, 127)),
        (0.60, new ArgbColor(219, 47, 75)),
        (0.72, new ArgbColor(255, 90, 31)),
        (0.84, new ArgbColor(255, 168, 46)),
        (0.94, new ArgbColor(255, 240, 100)),
        (1.00, new ArgbColor(255, 253, 240)),
    };

    public string Name => "Audition-like Spectrogram";
    public static readonly int[] Lut = BuildLut();

    private static int[] BuildLut()
    {
        var inst = new AuditionLikeSpectrogramColormap();
        var lut = new int[256];
        for (int i = 0; i < lut.Length; i++)
            lut[i] = unchecked((int)inst.GetColor(i / 255.0).ARGB);
        return lut;
    }

    public ArgbColor GetColor(double fraction)
    {
        if (!double.IsFinite(fraction))
            return Stops[0].Color;

        fraction = Math.Clamp(fraction, 0.0, 1.0);
        for (int i = 1; i < Stops.Length; i++)
        {
            if (fraction > Stops[i].Position) continue;

            var from = Stops[i - 1];
            var to = Stops[i];
            double t = (fraction - from.Position) / (to.Position - from.Position);
            return new ArgbColor(
                Lerp(from.Color.R, to.Color.R, t),
                Lerp(from.Color.G, to.Color.G, t),
                Lerp(from.Color.B, to.Color.B, t));
        }

        return Stops[^1].Color;
    }

    private static byte Lerp(byte from, byte to, double t)
        => (byte)Math.Round(from + (to - from) * t);
}
