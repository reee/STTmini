namespace STTmini.Core.Pipeline;

/// <summary>
/// 批量输出格式标志位（AGENTS.md §4.5 / §6.2）。
/// 支持组合（<see cref="Both"/>）以一次产出 .txt + .srt 两份。
/// </summary>
[Flags]
public enum BatchOutputFormat
{
    /// <summary>未选择任何格式（调用方应阻止进入运行）。</summary>
    None = 0,

    /// <summary>导出 .txt（纯文本，<see cref="Subtitles.PlainTextFormatter"/>）。</summary>
    Txt = 1,

    /// <summary>导出 .srt（字幕，<see cref="Subtitles.SrtFormatter"/>）。</summary>
    Srt = 2,

    /// <summary>两种格式都导出。</summary>
    Both = Txt | Srt,
}
