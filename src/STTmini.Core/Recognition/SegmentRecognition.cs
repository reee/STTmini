namespace STTmini.Core.Recognition;

/// <summary>
/// 带全局时间戳与段元信息的识别结果，供格式化器消费。
/// </summary>
/// <param name="GlobalStartSeconds">段全局起点（秒，相对整段音频起点）。</param>
/// <param name="GlobalEndSeconds">段全局终点（秒）。</param>
/// <param name="Result">该段的 ASR 原始输出。</param>
/// <param name="SilenceBeforeSeconds">与上一段之间的静音间隔（秒）；首段为 0。</param>
public sealed record SegmentRecognition(
    float GlobalStartSeconds,
    float GlobalEndSeconds,
    RecognitionResult Result,
    float SilenceBeforeSeconds);
