using System.IO;
using STTmini.Core.Pipeline;

namespace STTmini.Core.Tests.Pipeline;

/// <summary>
/// BatchOutputResolver 单测（AGENTS.md §4.5 / §6.2）：纯逻辑。
/// 约定：同目录、同 basename、换扩展名。
/// </summary>
public class BatchOutputResolverTests
{
    [Theory]
    [InlineData("C:\\videos\\trip.mp4", "txt", "C:\\videos\\trip.txt")]
    [InlineData("C:\\videos\\trip.mp4", "srt", "C:\\videos\\trip.srt")]
    [InlineData("/home/u/a/sound.flac", "txt", "/home/u/a/sound.txt")]
    public void ResolveOutputPath_ReplacesExtensionInPlace(string input, string ext, string expected)
    {
        Assert.Equal(expected, BatchOutputResolver.ResolveOutputPath(input, ext));
    }

    [Fact]
    public void ResolveOutputPath_PreservesDirectoryAndBasename()
    {
        string input = Path.Combine("D:", "media", "summer 2024", "clip 01.mov");
        string output = BatchOutputResolver.ResolveOutputPath(input, "srt");

        Assert.Equal(Path.Combine("D:", "media", "summer 2024", "clip 01.srt"), output);
    }

    [Fact]
    public void ResolveOutputPath_HandlesNoExtension()
    {
        // 无扩展名输入：ChangeExtension 行为 = 直接追加新扩展名。
        string output = BatchOutputResolver.ResolveOutputPath("/tmp/raw", "txt");
        Assert.Equal("/tmp/raw.txt", output);
    }
}
