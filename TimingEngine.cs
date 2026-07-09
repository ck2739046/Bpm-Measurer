using System;
using System.Collections.Generic;
using System.Linq;

namespace BpmMeasurer;

public static class TimingEngine
{
    public static IReadOnlyList<TimingPoint> RecalculateTiming(
        double offset,
        IReadOnlyList<RawTimingPoint> rawPoints)
    {
        if (rawPoints.Count == 0)
        {
            return new[] { new TimingPoint(Guid.NewGuid(), 0, 120, offset) };
        }

        var sorted = rawPoints.OrderBy(p => p.BeatIndex).ToList();
        var result = new List<TimingPoint>(sorted.Count);

        result.Add(new TimingPoint(sorted[0].Id, 0, sorted[0].Bpm, offset, sorted[0].MaxBeatIndex, sorted[0].BeatsPerBar));

        for (int i = 1; i < sorted.Count; i++)
        {
            var prev = result[i - 1];
            var curr = sorted[i];

            var beatDiff = curr.BeatIndex - prev.BeatIndex;
            var duration = beatDiff * (60.0 / prev.Bpm);

            result.Add(new TimingPoint(curr.Id, curr.BeatIndex, curr.Bpm, prev.Time + duration, curr.MaxBeatIndex, curr.BeatsPerBar));
        }

        return result;
    }

    public static (TimingPoint Point, int Index) GetPointAtTime(
        double time,
        IReadOnlyList<TimingPoint> points)
    {
        for (int i = points.Count - 1; i >= 0; i--)
        {
            if (time >= points[i].Time)
                return (points[i], i);
        }
        return (points[0], 0);
    }

    public static double GetBeatIndexAtTime(
        double time,
        IReadOnlyList<TimingPoint> points)
    {
        var (point, _) = GetPointAtTime(time, points);
        var timeDiff = time - point.Time;
        var secondsPerBeat = 60.0 / point.Bpm;
        return point.BeatIndex + (timeDiff / secondsPerBeat);
    }

    public static double GetTimeAtBeatIndex(
        double beatIndex,
        IReadOnlyList<TimingPoint> points)
    {
        var point = points[0];
        for (int i = points.Count - 1; i >= 0; i--)
        {
            if (beatIndex >= points[i].BeatIndex)
            {
                point = points[i];
                break;
            }
        }

        var beatDiff = beatIndex - point.BeatIndex;
        return point.Time + beatDiff * (60.0 / point.Bpm);
    }

    /// <summary>
    /// 找到包含 <paramref name="beatIndex"/> 的变速段：从后往前首个
    /// <c>beatIndex &gt;= points[i].BeatIndex</c>。段起点恒属该段（含等号边界）。
    /// </summary>
    public static (TimingPoint Point, int Index) FindSegmentForBeat(
        double beatIndex,
        IReadOnlyList<TimingPoint> points)
    {
        for (int i = points.Count - 1; i >= 0; i--)
        {
            if (beatIndex >= points[i].BeatIndex)
                return (points[i], i);
        }
        return (points[0], 0);
    }

    /// <summary>
    /// 返回严格大于 <paramref name="beatIndex"/> 的下一个节拍器网格拍。
    /// 取「当前段下一拍 (i+1)」与「下一段起点」中的较小者，因此每个变速段起点
    /// （强拍）永远不会被跨过——即便它是浮点（如 20.5）。
    /// </summary>
    public static double NextGridBeat(
        double beatIndex,
        IReadOnlyList<TimingPoint> points)
    {
        var (_, segIdx) = FindSegmentForBeat(beatIndex, points);
        double candidateA = beatIndex + 1.0;
        double candidateB = (segIdx + 1 < points.Count)
            ? points[segIdx + 1].BeatIndex
            : double.PositiveInfinity;
        return Math.Min(candidateA, candidateB);
    }

    /// <summary>
    /// 返回 &gt;= <paramref name="beatIndex"/> 的下一个网格拍（含当前拍），用于首次武装。
    /// 在所属段内对齐到 <c>segStart + k</c>（1e-9 容差吸收浮点噪声），并与下一段起点取较小者。
    /// </summary>
    public static double FirstGridBeatAtOrAfter(
        double beatIndex,
        IReadOnlyList<TimingPoint> points)
    {
        var (seg, segIdx) = FindSegmentForBeat(beatIndex, points);
        double segStart = seg.BeatIndex;
        double candidateA = segStart + Math.Ceiling(beatIndex - segStart - 1e-9);
        double candidateB = (segIdx + 1 < points.Count)
            ? points[segIdx + 1].BeatIndex
            : double.PositiveInfinity;
        return Math.Min(candidateA, candidateB);
    }
}
