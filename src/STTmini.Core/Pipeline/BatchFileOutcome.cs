namespace STTmini.Core.Pipeline;

/// <summary>
/// 单个输入文件在批量转录中的结局（AGENTS.md §4.5 / §6.3）。
/// </summary>
/// <param name="InputPath">输入文件全路径。</param>
/// <param name="Success">是否成功（失败则 <paramref name="Error"/> 给出原因）。</param>
/// <param name="Error">失败原因（成功时为 null）。</param>
/// <param name="OutputPaths">实际写出的输出文件全路径列表（成功时 ≥1，失败时为空）。</param>
public sealed record BatchFileOutcome(
    string InputPath,
    bool Success,
    string? Error,
    IReadOnlyList<string> OutputPaths)
{
    /// <summary>成功态构造便捷方法。</summary>
    public static BatchFileOutcome Succeeded(string inputPath, IReadOnlyList<string> outputPaths)
        => new(inputPath, Success: true, Error: null, outputPaths);

    /// <summary>失败态构造便捷方法。</summary>
    public static BatchFileOutcome Failed(string inputPath, string error)
        => new(inputPath, Success: false, Error: error, Array.Empty<string>());
}
