using System.IO;

namespace STTmini.Core.Pipeline;

/// <summary>
/// 批量输出路径解析（AGENTS.md §4.5 / §6.2）。纯逻辑、可测试。
/// 约定：输出与输入同目录、同 basename、换扩展名（<c>video.mp4</c> → <c>video.txt</c> / <c>video.srt</c>）。
/// </summary>
public static class BatchOutputResolver
{
    /// <summary>把输入文件路径改写为给定扩展名的同目录输出路径。</summary>
    /// <param name="inputPath">输入文件全路径。</param>
    /// <param name="extension">目标扩展名（不含点，如 <c>"txt"</c> / <c>"srt"</c>）。</param>
    public static string ResolveOutputPath(string inputPath, string extension)
        => Path.ChangeExtension(inputPath, "." + extension);
}
