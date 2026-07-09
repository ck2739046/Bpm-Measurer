using System;
using System.Windows;
using Un4seen.Bass;

namespace BpmMeasurer;

/// <summary>
/// 节拍器调度：在 <c>_bgmStream</c> 上用 BASS mixtime POS sync 实现采样级精确的点击触发，
/// 按每段 BeatsPerBar 分组（首拍强、其余弱），每个变速段起点恒为强拍（小节计数按段重置）。
/// 前提：播放期间 timing 不变（任何 seek / 改 BPM / 改 offset 都先暂停），故无需 seek 纠错。
/// </summary>
public partial class MainWindow
{
    // ── Metronome state ──
    private bool _metronomeEnabled = true;
    private MetronomeClickAsset[]? _metronomeClicks;
    /// <summary>beatIndex (encoded as long via BitConverter) → BASS sync handle；仅含已武装、尚未清理的拍（仅 UI 线程访问）。</summary>
    private readonly Dictionary<long, int> _beatSyncs = new();
    /// <summary>下一个待武装的拍号；NaN = 无武装（从当前位置起重算）。</summary>
    private double _armedUntilBeat = double.NaN;

    private const double ArmHorizonSec = 3.0;      // 向前武装的时间窗口
    private const double RefillThresholdSec = 1.0;  // 已武装到此秒以内则补武装

    private SYNCPROC? _metronomeSyncProc;

    // ── Click sample lifecycle ──

    private void EnsureMetronomeClicks()
    {
        if (_metronomeClicks == null)
            _metronomeClicks = MetronomeClick.CreateAll(44100);
    }

    private bool HasClicks => _metronomeClicks != null && _metronomeClicks.Length != 0;

    private void FreeMetronomeClicks()
    {
        ClearMetronomeSyncs();
        if (_metronomeClicks == null) return;
        foreach (var asset in _metronomeClicks)
            asset.Dispose();
        _metronomeClicks = null;
    }

    // ── Accent: per-segment BeatsPerBar grouping, reset per timing segment ──

    private ClickAccent GetAccentForBeat(double beatIndex)
    {
        if (_timingPoints.Count == 0) return ClickAccent.Weak;
        var point = _timingPoints[0];
        for (int i = _timingPoints.Count - 1; i >= 0; i--)
        {
            if (_timingPoints[i].BeatIndex <= beatIndex)
            {
                point = _timingPoints[i];
                break;
            }
        }
        double segStart = point.BeatIndex;
        int num = point.BeatsPerBar > 0 ? point.BeatsPerBar : 4;
        int inMeasure = (int)(((beatIndex - segStart) % num + num) % num);
        return inMeasure switch
        {
            0 => ClickAccent.Strong,   // 段起点 / 第 1 拍
            _ => ClickAccent.Weak,     // 其余拍
        };
    }

    // ── Arming ──

    /// <summary>
    /// 在 _bgmStream 上为 (currentPos, currentPos + ArmHorizon] 内、_armedUntilBeat 之后的
    /// 每个整数拍挂一个 ONETIME mixtime POS sync。
    /// </summary>
    private void ArmMetronome(double currentPos)
    {
        if (_bgmStream == 0 || _timingPoints.Count == 0 || !HasClicks) return;

        // 网格拍按段对齐：段起点可能是浮点（如 20.5），不可按全局整数拍步进，
        // 否则跨段后武装的拍会落到非网格位置造成错位。
        double i;
        if (double.IsNaN(_armedUntilBeat))
            i = TimingEngine.FirstGridBeatAtOrAfter(
                TimingEngine.GetBeatIndexAtTime(currentPos, _timingPoints), _timingPoints);
        else
            i = _armedUntilBeat;
        double untilTime = currentPos + ArmHorizonSec;

        int guard = 0;
        while (guard++ < 500)
        {
            double beatTime = TimingEngine.GetTimeAtBeatIndex(i, _timingPoints);
            if (beatTime > untilTime) break;
            if (beatTime >= currentPos)   // 跳过已过去的拍（mixtime 不会回放）
                ArmOneBeat(i, beatTime);
            i = TimingEngine.NextGridBeat(i, _timingPoints);
            _armedUntilBeat = i;
        }

        PrunePastBeats(currentPos);
    }

    private void ArmOneBeat(double beatIndex, double beatTime)
    {
        long bytePos = Bass.BASS_ChannelSeconds2Bytes(_bgmStream, beatTime);
        _metronomeSyncProc ??= MetronomeSyncProc;
        long encodedBeat = BitConverter.DoubleToInt64Bits(beatIndex);
        int sync = Bass.BASS_ChannelSetSync(
            _bgmStream,
            BASSSync.BASS_SYNC_POS | BASSSync.BASS_SYNC_MIXTIME | BASSSync.BASS_SYNC_ONETIME,
            bytePos, _metronomeSyncProc, (IntPtr)encodedBeat);
        if (sync != 0)
            _beatSyncs[encodedBeat] = sync;
    }

    /// <summary>从 BASS 与字典中移除全部已武装 sync，重置武装游标。</summary>
    private void ClearMetronomeSyncs()
    {
        if (_bgmStream != 0)
        {
            foreach (var sync in _beatSyncs.Values)
                Bass.BASS_ChannelRemoveSync(_bgmStream, sync);
        }
        _beatSyncs.Clear();
        _armedUntilBeat = double.NaN;
    }

    /// <summary>字典过大时剪掉已过去的拍（ONETIME 已自动从 BASS 移除，余者将在 pause 时清理）。</summary>
    private void PrunePastBeats(double currentPos)
    {
        if (_beatSyncs.Count <= 64) return;
        var stale = new List<long>();
        foreach (var kv in _beatSyncs)
        {
            double beatIndex = BitConverter.Int64BitsToDouble(kv.Key);
            double t = TimingEngine.GetTimeAtBeatIndex(beatIndex, _timingPoints);
            if (t < currentPos - 0.5) stale.Add(kv.Key);
        }
        foreach (var b in stale) _beatSyncs.Remove(b);
    }

    /// <summary>每帧滚动补武装：若已武装到 currentPos+1s 以内，补到 currentPos+3s。</summary>
    private void RefillMetronomeIfNeeded(double currentPos)
    {
        if (!_metronomeEnabled || !HasClicks || _bgmStream == 0) return;
        if (double.IsNaN(_armedUntilBeat)) { ArmMetronome(currentPos); return; }
        double armedUntilTime = TimingEngine.GetTimeAtBeatIndex(_armedUntilBeat, _timingPoints);
        if (armedUntilTime < currentPos + RefillThresholdSec)
            ArmMetronome(currentPos);
    }

    // ── BASS sync callback (mixtime: 渲染到达字节位置时触发；只读稳定状态，无锁) ──

    private void MetronomeSyncProc(int handle, int channel, int data, IntPtr user)
    {
        if (!HasClicks) return;
        var clicks = _metronomeClicks!;
        double beatIndex = BitConverter.Int64BitsToDouble((long)user);
        var accent = GetAccentForBeat(beatIndex);   // 纯函数：只读 _timingPoints（播放中不变）
        int assetIdx = (int)accent;
        if (assetIdx < 0 || assetIdx >= clicks.Length) return;
        int ch = Bass.BASS_SampleGetChannel(clicks[assetIdx].Handle, BASSFlag.BASS_DEFAULT);
        if (ch != 0)
        {
            Bass.BASS_ChannelSetAttribute(ch, BASSAttribute.BASS_ATTRIB_VOL, (float)_clickVol);
            Bass.BASS_ChannelPlay(ch, true);
        }
    }
}
