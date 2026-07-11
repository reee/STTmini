namespace STTmini.Core.Models;

/// <summary>
/// 模型文件集合的描述（AGENTS.md §9.1）。
/// </summary>
public static class ModelFileNames
{
    public const string ParaformerModel = "model.int8.onnx";
    public const string ParaformerTokens = "tokens.txt";
    public const string ParaformerAmvn = "am.mvn";
    public const string SileroVad = "silero_vad.onnx";
}
