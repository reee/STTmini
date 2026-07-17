namespace STTmini.Core.Pipeline;

/// <summary>
/// 批量进度报告 DTO（AGENTS.md §4.5 / §6.3）。
/// 把内层单文件 <see cref="TranscriptionProgress"/> 转译为带文件上下文的外层进度：
/// 顶行「批量转录中…（文件 i / 总 N：xxx.mp4）」，次行「识别中…（段 j / 总 M）」。
/// </summary>
/// <param name="CurrentFileIndex">当前文件序号（从 1 起）。</param>
/// <param name="TotalFiles">本次批量输入文件总数。</param>
/// <param name="CurrentFileName">当前文件名（无路径，便于显示）。</param>
/// <param name="Stage">当前文件所处的单文件流水线阶段。</param>
/// <param name="CurrentSegment">当前文件段进度（仅 <see cref="TranscriptionStage.Recognizing"/> 有意义）。</param>
/// <param name="TotalSegments">当前文件段总数。</param>
/// <param name="JustCompleted">某文件刚结束（成功或失败）时非空，驱动 UI 列表行状态切换；其它报告为 null。</param>
public sealed record BatchTranscriptionProgress(
    int CurrentFileIndex,
    int TotalFiles,
    string? CurrentFileName,
    TranscriptionStage Stage,
    int CurrentSegment,
    int TotalSegments,
    BatchFileOutcome? JustCompleted = null);
