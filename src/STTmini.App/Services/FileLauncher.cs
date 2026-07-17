using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace STTmini.App.Services;

/// <summary>
/// <see cref="IFileLauncher"/> 的 Avalonia 实现（LauncherExtensions API）。
/// 从当前应用主窗口取 TopLevel → Launcher。文件/目录自动分流。
/// </summary>
public sealed class FileLauncher : IFileLauncher
{
    private static TopLevel? TopLevel =>
        (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)
        ?.MainWindow;

    /// <inheritdoc/>
    public async Task<bool> OpenAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var tl = TopLevel;
        var launcher = tl?.Launcher;
        if (launcher is null)
        {
            return false;
        }

        try
        {
            // 目录优先判断（目录也可能不存在于 path，但 OpenAsync 调用方保证产出已写盘）。
            if (Directory.Exists(path))
            {
                await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
                return true;
            }

            if (File.Exists(path))
            {
                await launcher.LaunchFileInfoAsync(new FileInfo(path));
                return true;
            }

            return false;
        }
        catch
        {
            // 不同平台默认关联程序缺失会抛；统一吞掉返回 false，调用方静默。
            return false;
        }
    }
}
