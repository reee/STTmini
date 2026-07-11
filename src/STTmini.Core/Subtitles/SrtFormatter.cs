using System.Text;
using STTmini.Core.Recognition;

namespace STTmini.Core.Subtitles;

/// <summary>
/// SRT 字幕格式化（AGENTS.md §4.2 / §5.2）。纯逻辑。
/// 规则：单 VAD 段（或重切子段）→ 一个 cue；cue 边界由 token 时间戳决定。
/// </summary>
public static class SrtFormatter
{
    /// <summary>
    /// 将各段识别结果格式化为 SRT 文本。
    /// </summary>
    /// <param name="segments">按时间顺序的段识别结果（全局时间戳已计算）。</param>
    /// <returns>完整的 SRT 文本（UTF-8，以换行结尾）。</returns>
    public static string Format(IEnumerable<SegmentRecognition> segments)
    {
        var sb = new StringBuilder();
        int index = 1;
        foreach (var seg in segments)
        {
            // 全局 token 时间戳 = 段起点 + 段内相对时间戳
            var globalTs = TimestampMath.ToGlobal(seg.Result.Timestamps, seg.GlobalStartSeconds);
            var (start, end) = TimestampMath.CueBounds(globalTs, seg.GlobalStartSeconds, seg.GlobalEndSeconds);

            sb.Append(index).Append('\n');
            sb.Append(TimestampMath.FormatSrtTimecode(start))
              .Append(" --> ")
              .Append(TimestampMath.FormatSrtTimecode(end))
              .Append('\n');
            sb.Append(seg.Result.Text.Trim()).Append('\n');
            sb.Append('\n');

            index++;
        }

        return sb.ToString();
    }
}
