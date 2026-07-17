using System.IO;

namespace STTmini.Core.Audio;

/// <summary>
/// 批量输入展开器（AGENTS.md §4.5）。纯逻辑、可测试。
/// 把用户拖放/选择的混合路径列表（文件 + 文件夹）展开为去重的媒体文件全路径列表。
/// 文件夹仅扫顶层（v1 不递归子目录，符合「选一个视频文件夹」心智）。
/// 扩展名白名单与 UI 文件选择对话框保持一致（§6.2）。
/// </summary>
public static class BatchInputCollector
{
    /// <summary>
    /// 受支持的媒体扩展名（小写、不带点）。与 MainWindowViewModel.PickInputFileAsync 的 picker 过滤一致。
    /// </summary>
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".webm",
        ".mp3", ".wav", ".m4a", ".flac", ".aac",
    };

    /// <summary>
    /// 把混合路径（文件 / 文件夹）展开为去重的媒体文件全路径列表，按路径字典序稳定排序。
    /// 不存在的路径、非媒体扩展名的文件、空文件夹一律静默跳过。
    /// </summary>
    /// <remarks>
    /// 顺序约定：按「先展开再统一排序」保证多文件夹混合时顺序稳定，便于进度与结果对齐。
    /// </remarks>
    public static IReadOnlyList<string> Collect(IEnumerable<string> paths)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (Directory.Exists(raw))
            {
                // 文件夹：仅枚举顶层文件（v1 不递归）。EnumerateFiles 抛 IOException 时静默跳过该目录。
                IEnumerable<string> entries;
                try
                {
                    entries = Directory.EnumerateFiles(raw);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var file in entries)
                {
                    AddIfSupported(file, found, result);
                }
            }
            else if (File.Exists(raw))
            {
                AddIfSupported(raw, found, result);
            }
            // 不存在的路径静默忽略。
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>判断给定路径扩展名是否在 <see cref="SupportedExtensions"/> 白名单内。</summary>
    public static bool IsSupported(string path)
        => SupportedExtensions.Contains(Path.GetExtension(path));

    private static void AddIfSupported(string file, HashSet<string> seen, List<string> result)
    {
        if (!IsSupported(file))
        {
            return;
        }

        // 规范化为全路径再去重，避免 "C:\a.mp4" 与 "C:/a.mp4" 被当成两个文件。
        string full;
        try
        {
            full = Path.GetFullPath(file);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return;
        }

        if (seen.Add(full))
        {
            result.Add(full);
        }
    }
}
