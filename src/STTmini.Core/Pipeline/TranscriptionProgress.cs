using STTmini.Core.Recognition;

namespace STTmini.Core.Pipeline;

/// <summary>
/// 流水线阶段（AGENTS.md §6.3）。用于分阶段真实进度反馈。
/// </summary>
public enum TranscriptionStage
{
    Idle = 0,
    DecodingAudio,
    VoiceActivityDetection,
    Recognizing,
    Formatting,
    Done,
    Canceled,
    Failed,
}

/// <summary>
/// 进度报告 DTO（AGENTS.md §6.3 / §7）。
/// </summary>
public sealed record TranscriptionProgress(
    TranscriptionStage Stage,
    string Label,
    int CurrentSegment,
    int TotalSegments,
    SegmentRecognition? LatestSegment = null)
{
    /// <summary>阶段化的中文标签工厂（AGENTS.md §6.3）。</summary>
    public static string LabelFor(TranscriptionStage stage, int currentSegment = 0, int totalSegments = 0)
        => stage switch
        {
            TranscriptionStage.DecodingAudio => "解码音频…",
            TranscriptionStage.VoiceActivityDetection => "语音活动检测…",
            TranscriptionStage.Recognizing => totalSegments > 0
                ? $"识别中…（段 {currentSegment} / 总 {totalSegments}）"
                : "识别中…",
            TranscriptionStage.Formatting => "格式化输出…",
            TranscriptionStage.Done => "完成",
            TranscriptionStage.Canceled => "已取消",
            TranscriptionStage.Failed => "失败",
            _ => string.Empty,
        };
}
