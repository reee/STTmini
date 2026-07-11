using System.Collections.Generic;

namespace STTmini.Core.Configuration;

/// <summary>
/// <see cref="OutputFormat"/> 的可枚举列表，供 UI 的 ComboBox 绑定。
/// </summary>
public static class OutputFormats
{
    /// <summary>全部输出格式（PlainText / Srt）。</summary>
    public static IReadOnlyList<OutputFormat> All { get; } =
    [
        OutputFormat.PlainText,
        OutputFormat.Srt,
    ];

    /// <summary>输出格式的中文名称（UI 显示用）。</summary>
    public static string GetDisplayName(OutputFormat format) => format switch
    {
        OutputFormat.PlainText => "纯文本",
        OutputFormat.Srt => "SRT 字幕",
        _ => format.ToString(),
    };
}
