namespace STTmini.Core.Pipeline;

/// <summary>
/// 批量转录总结果（AGENTS.md §4.5）。仅承载各文件结局；输出文件已在运行中即时写盘。
/// </summary>
public sealed record BatchTranscriptionResult(IReadOnlyList<BatchFileOutcome> Outcomes)
{
    /// <summary>成功文件数。</summary>
    public int SuccessCount => Outcomes.Count(o => o.Success);

    /// <summary>失败文件数。</summary>
    public int FailureCount => Outcomes.Count - SuccessCount;
}
