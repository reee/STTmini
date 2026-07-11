using System.Text.Json.Serialization;

namespace STTmini.Core.Configuration;

/// <summary>
/// 用户设置 POCO（AGENTS.md §8.2）。极简：仅三项。
/// </summary>
public sealed class Settings
{
    /// <summary>ffmpeg 路径覆盖；null 表示用 PATH 自动检测。</summary>
    public string? FfmpegPathOverride { get; set; }

    /// <summary>默认输出格式。</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OutputFormat DefaultOutputFormat { get; set; } = OutputFormat.PlainText;

    /// <summary>上次打开的输入文件所在目录；用于下次文件选择对话框定位。</summary>
    public string? LastInputDirectory { get; set; }
}
