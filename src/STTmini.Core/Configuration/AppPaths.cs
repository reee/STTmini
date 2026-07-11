using System.Runtime.InteropServices;

namespace STTmini.Core.Configuration;

/// <summary>
/// 平台相关的配置/日志目录解析（AGENTS.md §8.1 / §8.4）。
/// Windows = 程序运行目录（portable）；Linux = XDG。
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// 配置文件所在目录。
    /// Windows：程序运行目录（portable）。
    /// Linux：XDG 配置目录（~/.config）下的 STTmini 子目录。
    /// </summary>
    public static string ConfigDirectory =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? AppContext.BaseDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "STTmini");

    /// <summary>
    /// 配置文件完整路径（AGENTS.md §8.1）。
    /// Windows：&lt;程序运行目录&gt;/STTmini.settings.json（portable）。
    /// Linux：~/.config/STTmini/settings.json（XDG）。
    /// </summary>
    public static string SettingsFilePath =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(ConfigDirectory, "STTmini.settings.json")
            : Path.Combine(ConfigDirectory, "settings.json");

    /// <summary>
    /// 日志文件所在目录（AGENTS.md §8.4）。
    /// Windows：程序运行目录下的 logs/。
    /// Linux：XDG data 目录（~/.local/share）下的 STTmini/logs/。
    /// </summary>
    public static string LogDirectory =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STTmini", "logs");

    /// <summary>
    /// 模型目录（两平台都跟随程序目录，AGENTS.md §8.3）。
    /// 发布时模型在 &lt;程序目录&gt;/models/。开发时（dotnet run）程序目录在 bin/.../，
    /// 模型在仓库根 models/，故回退向上查找最近存在的 models/ 目录。
    /// </summary>
    public static string ModelDirectory => ResolveModelDirectory();

    private static string ResolveModelDirectory()
    {
        // 1) 程序目录（发布形态）
        var primary = Path.Combine(AppContext.BaseDirectory, "models");
        if (Directory.Exists(primary))
        {
            return primary;
        }

        // 2) 开发期：从程序目录向上找仓库根的 models/
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "models");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        // 3) 回退到默认（即便不存在，后续 EnsureAllPresent 会给出明确错误）
        return primary;
    }
}
