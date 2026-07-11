using STTmini.Core.Recognition;

namespace STTmini.Core.Pipeline;

/// <summary>
/// 一次完整转录的结果（AGENTS.md §4.1[6] / §6.2 step 3）。
/// 同时携带按段识别结果（供 UI 在纯文本/SRT 之间实时切换重排）与按初始格式格式化的文本。
/// </summary>
/// <param name="Segments">按时间顺序的段识别结果（含全局时间戳与段间静音）。</param>
/// <param name="FormattedText">按 <paramref name="RequestedFormat"/> 格式化好的文本。</param>
/// <param name="RequestedFormat">本次转录请求的输出格式。</param>
public sealed record TranscriptionResult(
    IReadOnlyList<SegmentRecognition> Segments,
    string FormattedText,
    Core.Configuration.OutputFormat RequestedFormat);
