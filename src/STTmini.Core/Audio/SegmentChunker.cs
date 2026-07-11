namespace STTmini.Core.Audio;

/// <summary>
/// 超长段重切（AGENTS.md §4.1[3] / §5.2）。纯逻辑。
/// 规则：段时长 &gt; <see cref="MaxSegmentSeconds"/> 且（无内部静音，由 VAD 已保证），
/// 则按固定窗口切为多个子段，每个子段独立识别、独立成 cue。
/// </summary>
public static class SegmentChunker
{
    /// <summary>单段最大时长（秒），超出即固定窗口切分。</summary>
    public const float MaxSegmentSeconds = 25f;

    /// <summary>
    /// 将一个 VAD 段切成若干子段；段时长 ≤ 上限时原样返回单元素序列。
    /// 每个子段携带相对整段音频的全局起点（= 段起点 + 子段内偏移）。
    /// </summary>
    /// <returns>子段列表（按时间顺序，至少一个）。</returns>
    public static IReadOnlyList<ChunkedSegment> Chunk(SpeechSegment segment)
    {
        var maxSamples = (int)(MaxSegmentSeconds * AudioConstants.SampleRate);
        if (segment.Samples.Length <= maxSamples)
        {
            return [new ChunkedSegment(segment.StartSeconds, segment.StartSeconds + segment.DurationSeconds, segment.Samples)];
        }

        var chunks = new List<ChunkedSegment>();
        int offset = 0;
        while (offset < segment.Samples.Length)
        {
            int take = Math.Min(maxSamples, segment.Samples.Length - offset);
            var sub = new float[take];
            Array.Copy(segment.Samples, offset, sub, 0, take);

            float globalStart = segment.StartSeconds + offset / (float)AudioConstants.SampleRate;
            float globalEnd = globalStart + take / (float)AudioConstants.SampleRate;
            chunks.Add(new ChunkedSegment(globalStart, globalEnd, sub));

            offset += take;
        }

        return chunks;
    }
}

/// <summary>
/// 重切后的子段，携带全局起点/终点。
/// </summary>
/// <param name="GlobalStartSeconds">子段全局起点（秒）。</param>
/// <param name="GlobalEndSeconds">子段全局终点（秒）。</param>
/// <param name="Samples">子段 PCM 样本。</param>
public sealed record ChunkedSegment(float GlobalStartSeconds, float GlobalEndSeconds, float[] Samples);
