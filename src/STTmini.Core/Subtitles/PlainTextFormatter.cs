using System.Text;
using STTmini.Core.Recognition;

namespace STTmini.Core.Subtitles;

/// <summary>
/// 纯文本格式化（AGENTS.md §5.3）。纯逻辑。
/// 规则：按段顺序拼接；相邻段静音间隔 &gt; 2 秒 → 插入空行（段落分隔），否则单换行。
/// </summary>
public static class PlainTextFormatter
{
    /// <summary>段落分隔的静音阈值（秒）。</summary>
    public const float ParagraphSilenceThresholdSeconds = 2f;

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
            var text = seg.Result.Text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            // 静音间隔 > 阈值 → 段落分隔（空行）；否则单换行。
            // 首段（SilenceBeforeSeconds == 0 且缓冲为空）不前置分隔。
            if (sb.Length > 0)
            {
                sb.Append(seg.SilenceBeforeSeconds > ParagraphSilenceThresholdSeconds ? "\n\n" : "\n");
            }

            sb.Append(text);
        }

        return sb.ToString();
    }
}
