namespace STTmini.App.Services;

/// <summary>
/// 用系统默认程序打开文件 / 文件夹的抽象（AGENTS.md §6.6 / §4.3 seam）。
/// 封装 Avalonia 的 <c>LauncherExtensions</c>，便于在不依赖 TopLevel 的逻辑里触发「打开产出」，
/// 也便于测试注入 stub。与 <c>IFilePickerService</c> 同属一道 seam。
/// </summary>
public interface IFileLauncher
{
    /// <summary>
    /// 用系统默认程序打开 <paramref name="path"/>（文件→默认应用；目录→文件管理器）。
    /// 失败（无关联程序 / 平台不支持）返回 false，不抛——调用方静默处理。
    /// </summary>
    Task<bool> OpenAsync(string path);
}
