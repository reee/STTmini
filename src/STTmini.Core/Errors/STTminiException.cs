namespace STTmini.Core.Errors;

/// <summary>
/// 所有 STTmini 自定义异常的基类。UI 层可按具体类型分别提示（AGENTS.md §11.1）。
/// </summary>
public abstract class STTminiException : Exception
{
    protected STTminiException(string message) : base(message) { }
    protected STTminiException(string message, Exception innerException) : base(message, innerException) { }
}
