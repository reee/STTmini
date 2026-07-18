namespace STTmini.Core.Recognition;

/// <summary>
/// 单段语音识别结果（Core 自有 DTO，不泄露 sherpa-onnx 结构体，见 AGENTS.md §4.3）。
/// </summary>
/// <param name="Text">全文（paraformer-zh int8 实测不输出标点，AGENTS.md §5.1）。</param>
/// <param name="Tokens">逐 token 文本。</param>
/// <param name="Timestamps">逐 token 时间戳（秒，段内相对）。</param>
public sealed record RecognitionResult(string Text, IReadOnlyList<string> Tokens, IReadOnlyList<float> Timestamps)
{
    /// <summary>
    /// token 与时间戳数量应一致；时间戳数量可为 0（纯静音段或空结果）。
    /// </summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}
