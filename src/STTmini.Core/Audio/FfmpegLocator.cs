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
    /// 转录管线（<c>FfmpegAudioExtractor</c>）走这条——缺 ffmpeg 是硬错误，应中断。
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

    /// <summary>
    /// 不抛版的 ffmpeg 解析：UI 层用来刷新「CTA 是否可用 / 状态提示」——缺 ffmpeg 不是硬错误，
    /// 只是禁用转录入口。调用方无需 try/catch，避免两处 ViewModel 各写一份相同的吞异常逻辑。
    /// </summary>
    /// <returns>解析结果：<see cref="FfmpegResolution.IsFound"/> 为 true 时 <see cref="FfmpegResolution.Path"/> 非空。</returns>
    public static FfmpegResolution TryResolve(string? ffmpegPathOverride)
    {
        try
        {
            return new FfmpegResolution(Resolve(ffmpegPathOverride), Error: null);
        }
        catch (FfmpegNotFoundException ex)
        {
            return new FfmpegResolution(Path: null, Error: ex.Message);
        }
    }

    /// <summary>
    /// <see cref="TryResolve"/> 的返回类型。解耦「是否找到」与「为什么没找到」：
    /// <c>IsFound</c> 供 CTA 禁用判断，<c>Error</c> 供设置页状态行展示具体原因。
    /// </summary>
    public sealed record FfmpegResolution(string? Path, string? Error)
    {
        /// <summary>是否解析到 ffmpeg 路径（<see cref="Path"/> 非空）。</summary>
        public bool IsFound => !string.IsNullOrEmpty(Path);
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
