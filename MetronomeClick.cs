using Un4seen.Bass;

namespace BpmMeasurer;

/// <summary>节拍器重音档位：强 / 弱（每小节首拍强，其余弱）。</summary>
public enum ClickAccent { Strong, Weak }

/// <summary>
/// 一个预渲染的节拍器点击音效（方波 + 指数衰减包络），已上传为 BASS sample。
/// </summary>
public sealed class MetronomeClickAsset : IDisposable
{
    public int Handle { get; }
    public ClickAccent Accent { get; }

    public MetronomeClickAsset(int handle, ClickAccent accent)
    {
        Handle = handle;
        Accent = accent;
    }

    public void Dispose()
    {
        if (Handle != 0) Bass.BASS_SampleFree(Handle);
    }
}

/// <summary>
/// 合成 2 档节拍器点击音效：强(1500Hz)/弱(1000Hz)，
/// 方波 0.1s 指数衰减，归一化峰值分别为 0.70 / 0.48。
/// </summary>
public static class MetronomeClick
{
    private const double DurationSec = 0.1;   // 单次点击时长
    private const double DecayTau = 0.025;    // 指数衰减时间常数（~4τ 到 0.018）

    private sealed record AccentSpec(ClickAccent Accent, double Frequency, double Peak);

    private static readonly AccentSpec[] Specs =
    {
        new(ClickAccent.Strong, 1500.0, 0.70),
        new(ClickAccent.Weak,   1000.0, 0.48),
    };

    /// <summary>
    /// 生成全部 2 档点击音效。返回数组按 <see cref="ClickAccent"/> 枚举值索引。
    /// 调用前需已完成 BASS_Init。失败时不抛异常（避免 async void 路径闪退），记录 Debug 并返回空数组。
    /// </summary>
    public static MetronomeClickAsset[] CreateAll(int sampleRate)
    {
        try
        {
            var assets = new MetronomeClickAsset[Specs.Length];
            foreach (var spec in Specs)
            {
                var pcm = Synthesize(spec, sampleRate);
                // length 为字节数：FLOAT sample 每样本 4 字节
                int handle = Bass.BASS_SampleCreate(
                    pcm.Length * 4, sampleRate, 1, 8,
                    BASSFlag.BASS_SAMPLE_FLOAT | BASSFlag.BASS_SAMPLE_OVER_POS);
                if (handle == 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Metronome] BASS_SampleCreate failed for {spec.Accent}: {Bass.BASS_ErrorGetCode()}");
                    return Array.Empty<MetronomeClickAsset>();
                }
                if (!Bass.BASS_SampleSetData(handle, pcm))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Metronome] BASS_SampleSetData failed for {spec.Accent}: {Bass.BASS_ErrorGetCode()}");
                    Bass.BASS_SampleFree(handle);
                    return Array.Empty<MetronomeClickAsset>();
                }
                assets[(int)spec.Accent] = new MetronomeClickAsset(handle, spec.Accent);
            }
            return assets;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Metronome] CreateAll exception: {ex}");
            return Array.Empty<MetronomeClickAsset>();
        }
    }

    private static float[] Synthesize(AccentSpec spec, int sampleRate)
    {
        int length = (int)(DurationSec * sampleRate);
        var pcm = new float[length];
        double phaseStep = 2.0 * Math.PI * spec.Frequency / sampleRate;
        double phase = 0.0;
        for (int i = 0; i < length; i++)
        {
            double square = Math.Sin(phase) >= 0 ? 1.0 : -1.0;       // 方波
            double env = Math.Exp(-i / (sampleRate * DecayTau));      // 指数衰减包络
            float sample = (float)(spec.Peak * env * square);
            pcm[i] = sample;
            phase += phaseStep;
        }
        return pcm;
    }
}
