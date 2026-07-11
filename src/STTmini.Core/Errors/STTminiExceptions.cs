namespace STTmini.Core.Errors;

/// <summary>
/// PATH 与设置中均未找到 ffmpeg（AGENTS.md §11.1）。
/// UI 提示："未找到 ffmpeg，请在 Settings 中配置路径"。
/// </summary>
public sealed class FfmpegNotFoundException : STTminiException
{
    public FfmpegNotFoundException(string message) : base(message) { }
}

/// <summary>
/// ffmpeg 返回非零退出码（AGENTS.md §5.4 / §11.1）。
/// 内含截断的 stderr 尾部（非全量日志）。
/// UI 提示："音频提取失败：<截断 stderr>"。
/// </summary>
public sealed class AudioExtractionException : STTminiException
{
    /// <summary>ffmpeg stderr 的尾部（已截断）。</summary>
    public string StderrTail { get; }

    public AudioExtractionException(string message, string stderrTail)
        : base(message)
    {
        StderrTail = stderrTail;
    }
}

/// <summary>
/// 模型文件缺失（AGENTS.md §11.1）。
/// UI 提示："模型文件缺失，请重新安装或检查程序目录"。
/// </summary>
public sealed class ModelNotFoundException : STTminiException
{
    /// <summary>缺失的模型文件相对/绝对路径。</summary>
    public string MissingPath { get; }

    public ModelNotFoundException(string message, string missingPath)
        : base(message)
    {
        MissingPath = missingPath;
    }
}

/// <summary>
/// sherpa-onnx 初始化失败（AGENTS.md §11.1）。
/// UI 提示："识别引擎初始化失败，详见日志"。
/// </summary>
public sealed class RecognizerInitializationException : STTminiException
{
    public RecognizerInitializationException(string message, Exception innerException)
        : base(message, innerException) { }
}
