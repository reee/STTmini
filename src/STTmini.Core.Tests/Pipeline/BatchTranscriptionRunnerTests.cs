using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using STTmini.Core.Errors;
using STTmini.Core.Pipeline;
using STTmini.Core.Recognition;

namespace STTmini.Core.Tests.Pipeline;

/// <summary>
/// BatchTranscriptionRunner 单测（AGENTS.md §4.5）：用 stub 引擎 + 内存采集输出，验证：
/// 正常顺序产出 / 失败跳过继续 / 取消传播 / 格式 flags 组合 / 进度转译 / JustCompleted 时机 / 异常文案映射。
/// </summary>
public class BatchTranscriptionRunnerTests
{
    [Fact]
    public async Task Run_AllSucceed_WritesOutputsForEveryFile()
    {
        var engine = new StubEngine(_ => new TranscriptionResult(
            [Seg("你好。")], "你好。"));
        var writer = new CapturingWriter();
        var runner = new BatchTranscriptionRunner(engine, writer, NullLogger<BatchTranscriptionRunner>.Instance);

        var inputs = new[] { "C:\\a\\1.mp4", "C:\\a\\2.mp4" };

        var result = await runner.RunAsync(inputs, BatchOutputFormat.Both, new NoProgress(), CancellationToken.None);

        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(2, result.Outcomes.Count);
        Assert.All(result.Outcomes, o => Assert.True(o.Success));

        // 每个文件产出 .txt + .srt 两份
        Assert.Contains("C:\\a\\1.txt", writer.Written.Keys);
        Assert.Contains("C:\\a\\1.srt", writer.Written.Keys);
        Assert.Contains("C:\\a\\2.txt", writer.Written.Keys);
        Assert.Contains("C:\\a\\2.srt", writer.Written.Keys);
    }

    [Fact]
    public async Task Run_TxtOnly_WritesOnlyTxt()
    {
        var engine = new StubEngine(_ => new TranscriptionResult([Seg("x")], "x"));
        var writer = new CapturingWriter();
        var runner = new BatchTranscriptionRunner(engine, writer, NullLogger<BatchTranscriptionRunner>.Instance);

        await runner.RunAsync(["a.mp4"], BatchOutputFormat.Txt, new NoProgress(), CancellationToken.None);

        Assert.Contains("a.txt", writer.Written.Keys);
        Assert.DoesNotContain("a.srt", writer.Written.Keys);
    }

    [Fact]
    public async Task Run_SrtOnly_WritesOnlySrt()
    {
        var engine = new StubEngine(_ => new TranscriptionResult([Seg("x")], "x"));
        var writer = new CapturingWriter();
        var runner = new BatchTranscriptionRunner(engine, writer, NullLogger<BatchTranscriptionRunner>.Instance);

        await runner.RunAsync(["a.mp4"], BatchOutputFormat.Srt, new NoProgress(), CancellationToken.None);

        Assert.Contains("a.srt", writer.Written.Keys);
        Assert.DoesNotContain("a.txt", writer.Written.Keys);
    }

    [Fact]
    public async Task Run_MiddleFileFails_SkipsAndContinues()
    {
        var engine = new StubEngine(path =>
        {
            if (path.Contains("bad"))
            {
                throw new AudioExtractionException("boom", "ffmpeg died");
            }
            return new TranscriptionResult([Seg("ok")], "ok");
        });
        var writer = new CapturingWriter();
        var runner = new BatchTranscriptionRunner(engine, writer, NullLogger<BatchTranscriptionRunner>.Instance);

        var result = await runner.RunAsync(
            ["a.mp4", "bad.mp4", "c.mp4"],
            BatchOutputFormat.Txt,
            new NoProgress(),
            CancellationToken.None);

        // 中间失败被记录但流程继续
        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);

        var failed = result.Outcomes.Single(o => !o.Success);
        Assert.Equal("bad.mp4", failed.InputPath);
        Assert.Contains("ffmpeg died", failed.Error); // AudioExtractionException 文案映射

        // 失败文件没有输出，前后文件都产出
        Assert.Contains("a.txt", writer.Written.Keys);
        Assert.Contains("c.txt", writer.Written.Keys);
        Assert.DoesNotContain("bad.txt", writer.Written.Keys);
    }

    [Fact]
    public async Task Run_MissingModels_FriendlyErrorMessage()
    {
        var engine = new StubEngine(_ => throw new ModelNotFoundException("缺模型", "/models/m.onnx"));
        var writer = new CapturingWriter();
        var runner = new BatchTranscriptionRunner(engine, writer, NullLogger<BatchTranscriptionRunner>.Instance);

        var result = await runner.RunAsync(["a.mp4"], BatchOutputFormat.Txt, new NoProgress(), CancellationToken.None);

        Assert.Equal(1, result.FailureCount);
        Assert.Equal("模型文件缺失", result.Outcomes[0].Error);
    }

    [Fact]
    public async Task Run_CancellationStops_AndPropagates()
    {
        using var cts = new CancellationTokenSource();
        var engine = new StubEngine(_ =>
        {
            cts.Cancel(); // 第一个文件识别中触发取消
            throw new OperationCanceledException(cts.Token);
        });
        var writer = new CapturingWriter();
        var runner = new BatchTranscriptionRunner(engine, writer, NullLogger<BatchTranscriptionRunner>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            runner.RunAsync(["a.mp4", "b.mp4"], BatchOutputFormat.Txt, new NoProgress(), cts.Token));

        // b.mp4 没有被处理（取消冒泡到 RunAsync 调用方）
        Assert.DoesNotContain("b.txt", writer.Written.Keys);
    }

    [Fact]
    public async Task Run_ReportsProgressWithFileContext()
    {
        var engine = new StubEngine(_ => new TranscriptionResult([Seg("x")], "x"));
        var writer = new CapturingWriter();
        var runner = new BatchTranscriptionRunner(engine, writer, NullLogger<BatchTranscriptionRunner>.Instance);
        var reported = new List<BatchTranscriptionProgress>();
        var progress = new CallbackProgress<BatchTranscriptionProgress>(reported.Add);

        await runner.RunAsync(["v1.mp4", "v2.mp4"], BatchOutputFormat.Txt, progress, CancellationToken.None);

        // 外层进度里带上了文件序号与文件名
        Assert.Contains(reported, p => p.CurrentFileIndex == 1 && p.TotalFiles == 2 && p.CurrentFileName == "v1.mp4");
        Assert.Contains(reported, p => p.CurrentFileIndex == 2 && p.TotalFiles == 2 && p.CurrentFileName == "v2.mp4");
    }

    [Fact]
    public async Task Run_ReportsJustCompletedAtFileBoundary()
    {
        var engine = new StubEngine(_ => new TranscriptionResult([Seg("x")], "x"));
        var runner = new BatchTranscriptionRunner(engine, new CapturingWriter(), NullLogger<BatchTranscriptionRunner>.Instance);
        var completed = new List<BatchFileOutcome>();
        var progress = new CallbackProgress<BatchTranscriptionProgress>(p =>
        {
            if (p.JustCompleted is { } outcome)
            {
                completed.Add(outcome);
            }
        });

        await runner.RunAsync(["v1.mp4", "v2.mp4"], BatchOutputFormat.Txt, progress, CancellationToken.None);

        // 两个文件各发一次 JustCompleted，顺序与输入一致
        Assert.Equal(2, completed.Count);
        Assert.True(completed.All(o => o.Success));
        Assert.Equal(["v1.mp4", "v2.mp4"], completed.Select(o => o.InputPath).ToArray());
    }

    [Fact]
    public async Task Run_NoneFormats_Throws()
    {
        var runner = new BatchTranscriptionRunner(
            new StubEngine(_ => new TranscriptionResult([Seg("x")], "x")),
            new CapturingWriter(),
            NullLogger<BatchTranscriptionRunner>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            runner.RunAsync(["a.mp4"], BatchOutputFormat.None, new NoProgress(), CancellationToken.None));
    }

    [Fact]
    public async Task Run_EmptyInput_Throws()
    {
        // 防御：VM 已守，但 runner 是公共入口，不应静默返回 0/0 汇总。
        var runner = new BatchTranscriptionRunner(
            new StubEngine(_ => new TranscriptionResult([Seg("x")], "x")),
            new CapturingWriter(),
            NullLogger<BatchTranscriptionRunner>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            runner.RunAsync([], BatchOutputFormat.Txt, new NoProgress(), CancellationToken.None));
    }

    [Fact]
    public async Task Run_EngineReportsRecognizingProgress_IsRelayedWithFileContext()
    {
        // 引擎在内部报告 Recognizing 段进度 5/12；外层应原样保留段进度并附上文件序号。
        var engine = new StubEngine((_, progress) =>
        {
            progress.Report(new TranscriptionProgress(
                TranscriptionStage.Recognizing,
                "识别中…（段 5 / 总 12）",
                CurrentSegment: 5,
                TotalSegments: 12));
        });
        var runner = new BatchTranscriptionRunner(engine, new CapturingWriter(), NullLogger<BatchTranscriptionRunner>.Instance);
        var reported = new List<BatchTranscriptionProgress>();
        var progress = new CallbackProgress<BatchTranscriptionProgress>(reported.Add);

        await runner.RunAsync(["v.mp4"], BatchOutputFormat.Txt, progress, CancellationToken.None);

        Assert.Contains(reported, p =>
            p.Stage == TranscriptionStage.Recognizing &&
            p.CurrentSegment == 5 && p.TotalSegments == 12 &&
            p.CurrentFileName == "v.mp4" && p.CurrentFileIndex == 1);
    }

    private static SegmentRecognition Seg(string text)
        => new(GlobalStartSeconds: 0f, GlobalEndSeconds: 1f,
            Result: new RecognitionResult(text, Array.Empty<string>(), new float[] { 0.1f }),
            SilenceBeforeSeconds: 0f);

    private sealed class StubEngine : ITranscriptionEngine
    {
        private readonly Func<string, IProgress<TranscriptionProgress>, TranscriptionResult> _impl;

        // 重载 1：简单实现，忽略 progress。
        public StubEngine(Func<string, TranscriptionResult> impl)
            => _impl = (path, _) => impl(path);

        // 重载 2：需要回调 progress 的实现（用于进度转译测试）。
        public StubEngine(Action<string, IProgress<TranscriptionProgress>> impl)
            => _impl = (path, p) => { impl(path, p); return new TranscriptionResult([Seg("x")], "x"); };

        public Task<TranscriptionResult> TranscribeAsync(string inputPath, IProgress<TranscriptionProgress> progress, CancellationToken cancellationToken)
        {
            return Task.FromResult(_impl(inputPath, progress));
        }
    }

    private sealed class CapturingWriter : IBatchOutputWriter
    {
        public ConcurrentDictionary<string, string> Written { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task WriteTextAsync(string path, string content, CancellationToken cancellationToken)
        {
            Written[path] = content;
            return Task.CompletedTask;
        }
    }

    private sealed class NoProgress : IProgress<BatchTranscriptionProgress>
    {
        public void Report(BatchTranscriptionProgress value) { }
    }

    private sealed class CallbackProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;
        public CallbackProgress(Action<T> onReport) => _onReport = onReport;
        public void Report(T value) => _onReport(value);
    }
}
