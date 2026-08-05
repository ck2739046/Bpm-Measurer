using Un4seen.Bass;

namespace BpmMeasurer;

public class BpmAudioData
{
    public string FilePath { get; init; } = "";
    /// <summary>已下混为单声道（mono）的 PCM 样本，访问连续、缓存友好。</summary>
    public short[] RawSamples { get; set; } = Array.Empty<short>();
    public int SampleRate { get; init; }
    /// <summary>原始声道数（仅显示用）；RawSamples 已下混为 mono。</summary>
    public int Channels { get; init; }
    public double Duration { get; init; }
}

public static class BpmAudioLoader
{
    public static BpmAudioData? Load(string filePath)
    {
        var stream = Bass.BASS_StreamCreateFile(filePath, 0L, 0L, BASSFlag.BASS_STREAM_DECODE);
        if (stream == 0)
        {
            System.Diagnostics.Debug.WriteLine($"BASS_StreamCreateFile failed: {Bass.BASS_ErrorGetCode()}");
            return null;
        }

        try
        {
            var info = Bass.BASS_ChannelGetInfo(stream);
            var length = Bass.BASS_ChannelGetLength(stream, BASSMode.BASS_POS_BYTE);
            var duration = Bass.BASS_ChannelBytes2Seconds(stream, length);

            int channels = info.chans;
            var totalSamples = (int)(length / sizeof(short));
            var raw = new short[totalSamples];

            int byteLen = (int)length;
            int bytesRead = Bass.BASS_ChannelGetData(stream, raw, byteLen);
            if (bytesRead < 0) bytesRead = 0;

            // 优化点4：加载阶段下混成 mono，后续波形访问连续、缓存友好，内存减半
            short[] samples = raw;
            if (channels > 1)
            {
                int monoLen = totalSamples / channels;
                samples = new short[monoLen];
                for (int i = 0; i < monoLen; i++)
                {
                    int baseIdx = i * channels;
                    int sum = 0;
                    for (int ch = 0; ch < channels; ch++)
                        sum += raw[baseIdx + ch];
                    samples[i] = (short)(sum / channels);
                }
            }

            return new BpmAudioData
            {
                FilePath = filePath,
                RawSamples = samples,
                SampleRate = info.freq,
                Channels = channels,
                Duration = duration
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            Bass.BASS_StreamFree(stream);
        }
    }
}
