namespace STTmini.Core.Subtitles;

/// <summary>
/// 时间戳计算（AGENTS.md §4.2 / §5.1）。纯逻辑。
/// 职责：全局时间戳 = 段/子段全局起点 + 段内 token 相对时间戳；cue 边界由 token 时间戳决定。
/// </summary>
public static class TimestampMath
{
    /// <summary>
    /// 将段内相对 token 时间戳加上全局偏移，得到全局时间戳。
    /// </summary>
    public static IReadOnlyList<float> ToGlobal(IReadOnlyList<float> relativeTimestamps, float globalOffsetSeconds)
    {
        if (relativeTimestamps.Count == 0)
        {
            return Array.Empty<float>();
        }

        var result = new float[relativeTimestamps.Count];
        for (int i = 0; i < relativeTimestamps.Count; i++)
        {
            result[i] = globalOffsetSeconds + relativeTimestamps[i];
        }

        return result;
    }

    /// <summary>
    /// 计算 cue 边界（AGENTS.md §5.2）。
    /// 起点 = 首 token 全局时间戳；终点 = 末 token 全局时间戳；
    /// 若 token 时间戳缺失，回退到子段全局边界。
    /// </summary>
    public static (float StartSeconds, float EndSeconds) CueBounds(
        IReadOnlyList<float> globalTokenTimestamps,
        float segmentGlobalStart,
        float segmentGlobalEnd)
    {
        if (globalTokenTimestamps.Count == 0)
        {
            return (segmentGlobalStart, segmentGlobalEnd);
        }

        float start = globalTokenTimestamps[0];
        float end = globalTokenTimestamps[^1];
        // 末 token 时间戳是 token 起点而非终点，可能等于或略小于实际段末；
        // 至少保证 end >= start，并以子段全局终点封顶（避免 cue 溢出）。
        if (end < start)
        {
            end = start;
        }

        return (start, Math.Min(end, segmentGlobalEnd));
    }

    /// <summary>
    /// 格式化为 SRT 时间码：HH:MM:SS,mmm（AGENTS.md §5.2）。
    /// </summary>
    public static string FormatSrtTimecode(float seconds)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        int totalMs = (int)Math.Round(seconds * 1000);
        int ms = totalMs % 1000;
        int totalSeconds = totalMs / 1000;
        int s = totalSeconds % 60;
        int totalMinutes = totalSeconds / 60;
        int m = totalMinutes % 60;
        int h = totalMinutes / 60;

        return $"{h:D2}:{m:D2}:{s:D2},{ms:D3}";
    }
}
