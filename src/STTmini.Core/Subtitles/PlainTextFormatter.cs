using System.Text;
using STTmini.Core.Recognition;

namespace STTmini.Core.Subtitles;

/// <summary>
/// 纯文本格式化（AGENTS.md §5.3）。纯逻辑。
/// 规则：按段顺序拼接；相邻段静音间隔 &gt; 0.6 秒 → 插入空行（段落分隔），否则单换行。
/// </summary>
public static class PlainTextFormatter
{
    /// <summary>
    /// 段落分隔的静音阈值（秒）。
    /// 取值依据：实测快语速视频句间停顿中位数约 0.47s、p75 约 0.67s；
    /// 原值 2s 在此类视频上永远触达不到（最大 gap ~0.95s），导致纯文本全部兜底单换行。
    /// 0.6s 取 p75 附近，能切出段落又不至于把句内停顿误判为段落断点。
    /// </summary>
    public const float ParagraphSilenceThresholdSeconds = 0.6f;

    /// <summary>
    /// 将各段识别结果格式化为纯文本。
    /// </summary>
    /// <param name="segments">按时间顺序的段识别结果，<see cref="SegmentRecognition.SilenceBeforeSeconds"/> 指示与上一段的静音间隔。</param>
    /// <returns>纯文本（UTF-8）。</returns>
    public static string Format(IEnumerable<SegmentRecognition> segments)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            AppendSegment(sb, seg, isFirst: sb.Length == 0);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 按 §5.3 规则把单段文本追加进缓冲区，供 UI 实时填充复用同一份逻辑（避免规则漂移）。
    /// </summary>
    /// <param name="buffer">待追加的缓冲区。</param>
    /// <param name="segment">要追加的段。</param>
    /// <param name="isFirst">是否为输出中的第一段（第一段不前置分隔）。</param>
    /// <returns>追加后是否仍为第一段（即本次跳过了空文本则不变）。</returns>
    public static bool AppendSegment(StringBuilder buffer, SegmentRecognition segment, bool isFirst)
    {
        var text = segment.Result.Text.Trim();
        if (text.Length == 0)
        {
            return isFirst;
        }

        if (!isFirst)
        {
            buffer.Append(segment.SilenceBeforeSeconds > ParagraphSilenceThresholdSeconds ? "\n\n" : "\n");
        }

        buffer.Append(text);
        return false;
    }
}
