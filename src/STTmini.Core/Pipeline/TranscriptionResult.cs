using STTmini.Core.Recognition;

namespace STTmini.Core.Pipeline;

/// <summary>
/// 一次完整转录的结果（AGENTS.md §4.1[6] / §6.2）。
/// 携带按段识别结果（含全局时间戳与段间静音）与纯文本预览；SRT 等其它格式由调用方
/// 按 <see cref="Segments"/> 即时格式化（AGENTS.md §6.2 双保存按钮）。
/// </summary>
/// <param name="Segments">按时间顺序的段识别结果（含全局时间戳与段间静音）。</param>
/// <param name="PlainText">纯文本预览（UI 主显示，AGENTS.md §5.3）。</param>
public sealed record TranscriptionResult(
    IReadOnlyList<SegmentRecognition> Segments,
    string PlainText);
