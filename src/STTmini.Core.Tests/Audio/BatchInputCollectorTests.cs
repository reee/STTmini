using System.IO;
using STTmini.Core.Audio;

namespace STTmini.Core.Tests.Audio;

/// <summary>
/// BatchInputCollector 单测（AGENTS.md §4.5）：纯逻辑，无副作用（用临时目录）。
/// 覆盖：文件直传、文件夹顶层展开、扩展名过滤、混合路径去重、空文件夹、不存在路径。
/// </summary>
public class BatchInputCollectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sttmini-batch-tests-" + Guid.NewGuid().ToString("N"));

    public BatchInputCollectorTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 测试清理容错 */ }
    }

    [Fact]
    public void Collect_PassesSupportedFilesThrough()
    {
        string mp4 = Touch("a.mp4");
        string mkv = Touch("b.mkv");

        var result = BatchInputCollector.Collect([mp4, mkv]);

        Assert.Equal(2, result.Count);
        Assert.Contains(mp4, result);
        Assert.Contains(mkv, result);
    }

    [Fact]
    public void Collect_FiltersOutUnsupportedExtensions()
    {
        string mp4 = Touch("a.mp4");
        string txt = Touch("a.txt");
        string png = Touch("pic.png");

        var result = BatchInputCollector.Collect([mp4, txt, png]);

        Assert.Single(result);
        Assert.Contains(mp4, result);
    }

    [Fact]
    public void Collect_ExpandsFolderTopLevelFilesOnly()
    {
        // 顶层两个媒体文件 + 一个非媒体 + 一个子目录里的媒体文件（应被忽略，v1 不递归）
        string top1 = Touch("data/v1.mp4");
        string top2 = Touch("data/v2.wav");
        Touch("data/readme.txt");
        string nested = Touch("data/sub/nested.mp4");

        var result = BatchInputCollector.Collect([Path.Combine(_root, "data")]);

        Assert.Contains(top1, result);
        Assert.Contains(top2, result);
        Assert.DoesNotContain(nested, result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Collect_DeduplicatesMixedFilesAndFolders()
    {
        // 同一个文件既直接传入又落在被展开的文件夹里 → 只出现一次
        string mp4 = Touch("data/x.mp4");

        var result = BatchInputCollector.Collect([mp4, Path.Combine(_root, "data")]);

        Assert.Single(result);
        Assert.Equal(Path.GetFullPath(mp4), result[0]);
    }

    [Fact]
    public void Collect_NormalizesPathSeparatorsBeforeDedup()
    {
        string mp4 = Touch("y.mp4");
        string withAltSep = mp4.Replace('\\', '/');

        var result = BatchInputCollector.Collect([mp4, withAltSep]);

        Assert.Single(result);
    }

    [Fact]
    public void Collect_EmptyFolder_YieldsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        var result = BatchInputCollector.Collect([Path.Combine(_root, "empty")]);

        Assert.Empty(result);
    }

    [Fact]
    public void Collect_NonExistentPaths_AreSilentlySkipped()
    {
        var result = BatchInputCollector.Collect([Path.Combine(_root, "nope.mp4"), Path.Combine(_root, "nope-dir")]);

        Assert.Empty(result);
    }

    [Fact]
    public void Collect_WhitespaceAndNullEntries_Ignored()
    {
        string mp4 = Touch("z.mp4");

        var result = BatchInputCollector.Collect([null!, "", "   ", mp4]);

        Assert.Single(result);
    }

    [Fact]
    public void Collect_ReturnsSortedOrder()
    {
        string b = Touch("b.mp4");
        string a = Touch("a.mp4");
        string c = Touch("c.mp4");

        var result = BatchInputCollector.Collect([c, a, b]);

        Assert.Equal([Path.GetFullPath(a), Path.GetFullPath(b), Path.GetFullPath(c)], result);
    }

    [Theory]
    [InlineData("x.MP4")]
    [InlineData("x.Mp4")]
    [InlineData("x.mkv")]
    public void IsSupported_IsCaseInsensitive(string name)
    {
        Assert.True(BatchInputCollector.IsSupported(name));
    }

    [Theory]
    [InlineData("x.txt")]
    [InlineData("x")]
    [InlineData("")]
    public void IsSupported_RejectsUnknown(string name)
    {
        Assert.False(BatchInputCollector.IsSupported(name));
    }

    private string Touch(string relativePath)
    {
        string full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string? dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(full, "x");
        return full;
    }
}
