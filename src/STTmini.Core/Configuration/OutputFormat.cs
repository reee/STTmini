namespace STTmini.Core.Configuration;

/// <summary>
/// 输出格式（AGENTS.md §8.2）。
/// </summary>
public enum OutputFormat
{
    /// <summary>纯文本（默认）。</summary>
    PlainText = 0,

    /// <summary>SRT 字幕。</summary>
    Srt = 1,
}
