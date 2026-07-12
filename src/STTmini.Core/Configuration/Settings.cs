namespace STTmini.Core.Configuration;

/// <summary>
/// 用户设置 POCO（AGENTS.md §8.2）。极简：仅两项。
/// （曾含 DefaultOutputFormat——转录结果现同时持有纯文本与 SRT，由主窗双保存按钮分别导出，该项已移除。）
/// </summary>
public sealed class Settings
{
    /// <summary>ffmpeg 路径覆盖；null 表示用 PATH 自动检测。</summary>
    public string? FfmpegPathOverride { get; set; }

    /// <summary>上次打开的输入文件所在目录；用于下次文件选择对话框定位。</summary>
    public string? LastInputDirectory { get; set; }
}
