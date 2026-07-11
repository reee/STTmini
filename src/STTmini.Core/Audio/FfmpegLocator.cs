using System.Diagnostics;
using System.Runtime.InteropServices;
using STTmini.Core.Errors;

namespace STTmini.Core.Audio;

/// <summary>
/// 定位 ffmpeg 可执行文件（AGENTS.md §5.4 / §6.5 / §11.1）。
/// 优先级：用户设置的 <c>ffmpegPathOverride</c>（可为目录或可执行文件路径）&gt; PATH 自动检测。
/// </summary>
public static class FfmpegLocator
{
    private const string ExeName = "ffmpeg";
    private const string WindowsExeName = "ffmpeg.exe";

    /// <summary>
    /// 解析 ffmpeg 可执行文件路径。找不到抛 <see cref="FfmpegNotFoundException"/>。
    /// </summary>
    public static string Resolve(string? ffmpegPathOverride)
    {
        // 1) 用户覆盖
        if (!string.IsNullOrWhiteSpace(ffmpegPathOverride))
        {
            var path = ResolveOverride(ffmpegPathOverride);
            if (path is not null)
            {
                return path;
            }
        }

        // 2) PATH 自动检测
        var fromPath = FindOnPath(ExeExecutableName);
        if (fromPath is not null)
        {
            return fromPath;
        }

        throw new FfmpegNotFoundException(
            "未找到 ffmpeg。请在 Settings 中配置 ffmpeg 路径，或将其加入系统 PATH。");
    }

    private static string? ResolveOverride(string overrideValue)
    {
        overrideValue = overrideValue.Trim();
        if (overrideValue.EndsWith(WindowsExeName, StringComparison.OrdinalIgnoreCase)
            || overrideValue.EndsWith("/" + ExeName) || overrideValue.EndsWith(ExeName) && !Directory.Exists(overrideValue))
        {
            return File.Exists(overrideValue) ? overrideValue : null;
        }

        // 视为目录：在其中查找 ffmpeg 可执行文件
        if (Directory.Exists(overrideValue))
        {
            var candidate = Path.Combine(overrideValue, ExeExecutableName);
            return File.Exists(candidate) ? candidate : null;
        }

        // 既非存在的文件也非目录，但仍可能直接指向可执行文件
        return File.Exists(overrideValue) ? overrideValue : null;
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(dir.Trim(), fileName);
            }
            catch { continue; }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string ExeExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsExeName : ExeName;
}
