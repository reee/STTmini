using Microsoft.Extensions.Logging.Abstractions;
using STTmini.Core.Audio;
using STTmini.Core.Errors;
using STTmini.Core.Pipeline;
using STTmini.Core.Recognition;

namespace STTmini.Core.Tests.Pipeline;

/// <summary>
/// 用 mock 组件验证 TranscriptionEngine 的编排逻辑（AGENTS.md §4.1 / §7），
/// 不依赖真实模型 / 原生库。
/// 引擎只产纯文本预览；SRT 的全局时间戳格式化由 <c>SrtFormatterTests</c> 单独覆盖。
/// </summary>
public class TranscriptionEngineTests
{
    private const int SampleRate = 16000;

    [Fact]
    public async Task Transcribe_ProducesPlainTextPreview()
    {
        var samples = new float[SampleRate * 1]; // 1 秒音频占位
        var engine = BuildEngine(
            samples,
            vadSegments: [new SpeechSegment(0f, samples)],
            recognizeImpl: s => new RecognitionResult("你好世界。", new[] { "你", "好", "世", "界" }, new float[] { 0.1f, 0.3f, 0.5f, 0.7f }));

        var result = await engine.TranscribeAsync(
            "fake.mp4",
            new NoProgress(),
            CancellationToken.None);

        Assert.Equal("你好世界。", result.PlainText);
        Assert.Single(result.Segments);
    }

    [Fact]
    public async Task Transcribe_ReportsProgressStages()
    {
        var reported = new List<TranscriptionProgress>();
        var progress = new ProgressCollector(reported);

        var engine = BuildEngine(
            new float[SampleRate],
            vadSegments: [new SpeechSegment(0f, new float[SampleRate])],
            recognizeImpl: _ => new RecognitionResult("x", Array.Empty<string>(), Array.Empty<float>()));

        await engine.TranscribeAsync("fake.mp4", progress, CancellationToken.None);

        var stages = reported.Select(p => p.Stage).ToArray();
        Assert.Contains(TranscriptionStage.DecodingAudio, stages);
        Assert.Contains(TranscriptionStage.VoiceActivityDetection, stages);
        Assert.Contains(TranscriptionStage.Recognizing, stages);
        Assert.Contains(TranscriptionStage.Formatting, stages);
        Assert.Contains(TranscriptionStage.Done, stages);
    }

    [Fact]
    public async Task Transcribe_CancelAtBatchBoundary_ThrowsOperationCanceled()
    {
        // 取消粒度：批边界（AGENTS.md §4.4 / §6.4）。
        // 两段（< BatchSize=8）并入一批：批内 Recognize 触发取消，
        // 批处理返回后在批边界 ThrowIfCancellationRequested 抛出。已识别段保留。
        var samples = new float[SampleRate];
        using var cts = new CancellationTokenSource();
        var engine = BuildEngine(
            samples,
            vadSegments:
            [
                new SpeechSegment(0f, samples),
                new SpeechSegment(2f, samples),
            ],
            recognizeImpl: s =>
            {
                cts.Cancel(); // 模拟在批内识别时被取消
                return new RecognitionResult("段。", Array.Empty<string>(), Array.Empty<float>());
            });

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            engine.TranscribeAsync("fake.mp4", new NoProgress(), cts.Token));
    }

    [Fact]
    public async Task Transcribe_UsesBatchRecognize_AndChunksByBatchSize()
    {
        // 25 段应分 4 批（8+8+8+1）调用 RecognizeMany，且结果按段顺序输出（AGENTS.md §4.4 方案 B）。
        // batchSize 直接引用 TranscriptionEngine.BatchSize，避免生产/测试各定义一份魔法数漂移。
        int batchSize = TranscriptionEngine.BatchSize;
        const int segmentCount = 25;
        var samples = new float[SampleRate];
        var vadSegments = Enumerable.Range(0, segmentCount)
            .Select(i => new SpeechSegment(i * 1f, samples))
            .ToList();

        var batchCallSizes = new List<int>();
        var engine = BuildEngineWithBatchCounter(
            samples,
            vadSegments,
            recognizeImpl: s => new RecognitionResult("段。", Array.Empty<string>(), Array.Empty<float>()),
            batchCallSizes);

        var result = await engine.TranscribeAsync("fake.mp4", new NoProgress(), CancellationToken.None);

        int expectedBatches = (segmentCount + batchSize - 1) / batchSize; // ceil(25/8) = 4
        Assert.Equal(expectedBatches, batchCallSizes.Count);
        // 前 3 批满 8 段，末批 1 段
        Assert.Equal(Enumerable.Repeat(batchSize, expectedBatches - 1).Append(segmentCount - batchSize * (expectedBatches - 1)),
            batchCallSizes);
        // 顺序保持：全部 25 段都被识别
        Assert.Equal(segmentCount, result.Segments.Count);
    }

    [Fact]
    public async Task Transcribe_OverlongSegment_GetsChunked()
    {
        // 30 秒 VAD 段 → SegmentChunker 切为 25s + 5s 两段
        var longSamples = new float[SampleRate * 30];
        var engine = BuildEngine(
            new float[SampleRate],
            vadSegments: [new SpeechSegment(0f, longSamples)],
            recognizeImpl: _ => new RecognitionResult("内容。", Array.Empty<string>(), Array.Empty<float>()));

        var result = await engine.TranscribeAsync(
            "fake.mp4", new NoProgress(), CancellationToken.None);

        // 两段都识别为"内容。"，静音间隔由子段邻接决定（≈0 → 单换行）
        Assert.Equal("内容。\n内容。", result.PlainText);
        Assert.Equal(2, result.Segments.Count);
    }

    [Fact]
    public async Task Transcribe_MissingModels_ThrowsModelNotFoundException_AgentsMd_11_1()
    {
        // EnsureModelsPresent 抛 ModelNotFoundException 时，应优先于原生初始化冒泡（AGENTS.md §11.1）。
        var engine = BuildEngine(
            new float[SampleRate],
            vadSegments: [new SpeechSegment(0f, new float[SampleRate])],
            recognizeImpl: _ => new RecognitionResult("x", Array.Empty<string>(), Array.Empty<float>()),
            modelsPresent: false);

        await Assert.ThrowsAsync<ModelNotFoundException>(() =>
            engine.TranscribeAsync("fake.mp4", new NoProgress(), CancellationToken.None));
    }

    // ---- helpers ----

    private static TranscriptionEngine BuildEngine(
        float[] extractedSamples,
        IReadOnlyList<SpeechSegment> vadSegments,
        Func<float[], RecognitionResult> recognizeImpl,
        bool modelsPresent = true)
    {
        var extractor = new StubAudioExtractor(extractedSamples);
        var factory = new StubComponentsFactory(vadSegments, recognizeImpl, modelsPresent);
        return new TranscriptionEngine(extractor, factory, NullLogger<TranscriptionEngine>.Instance);
    }

    /// <summary>
    /// 构造引擎，并在其 recognizer 上记录每次 RecognizeMany 的批大小
    /// （用于验证 TranscriptionEngine 确实走了 batch 路径并按 BatchSize 切批）。
    /// </summary>
    private static TranscriptionEngine BuildEngineWithBatchCounter(
        float[] extractedSamples,
        IReadOnlyList<SpeechSegment> vadSegments,
        Func<float[], RecognitionResult> recognizeImpl,
        List<int> batchCallSizes)
    {
        var extractor = new StubAudioExtractor(extractedSamples);
        var factory = new StubComponentsFactory(vadSegments, recognizeImpl, modelsPresent: true, batchCallSizes);
        return new TranscriptionEngine(extractor, factory, NullLogger<TranscriptionEngine>.Instance);
    }

    private sealed class StubAudioExtractor(float[] samples) : IAudioExtractor
    {
        public Task<float[]> ExtractAsync(string inputPath, CancellationToken cancellationToken)
            => Task.FromResult(samples);
    }

    private sealed class StubComponentsFactory : ITranscriptionComponentsFactory
    {
        private readonly IReadOnlyList<SpeechSegment> _vadSegments;
        private readonly Func<float[], RecognitionResult> _recognize;
        private readonly bool _modelsPresent;
        private readonly List<int>? _batchCallSizes;

        public StubComponentsFactory(
            IReadOnlyList<SpeechSegment> vadSegments,
            Func<float[], RecognitionResult> recognize,
            bool modelsPresent,
            List<int>? batchCallSizes = null)
        {
            _vadSegments = vadSegments;
            _recognize = recognize;
            _modelsPresent = modelsPresent;
            _batchCallSizes = batchCallSizes;
        }

        public IRecognizer CreateRecognizer() => new StubRecognizer(_recognize, _batchCallSizes);
        public IVoiceActivityDetector CreateVoiceActivityDetector() => new StubVad(_vadSegments);

        public void EnsureModelsPresent()
        {
            if (!_modelsPresent)
            {
                throw new ModelNotFoundException("测试：模型缺失", "/models/model.int8.onnx");
            }
        }
    }

    private sealed class StubRecognizer : IRecognizer
    {
        private readonly Func<float[], RecognitionResult> _impl;
        private readonly List<int>? _batchCallSizes;

        public StubRecognizer(Func<float[], RecognitionResult> impl, List<int>? batchCallSizes = null)
        {
            _impl = impl;
            _batchCallSizes = batchCallSizes;
        }

        public RecognitionResult Recognize(float[] samples) => _impl(samples);

        // 覆盖默认 RecognizeMany：记录批大小，便于测试断言 batch 路径被实际走通。
        public IReadOnlyList<RecognitionResult> RecognizeMany(IReadOnlyList<float[]> batches)
        {
            _batchCallSizes?.Add(batches.Count);
            return batches.Select(_impl).ToList();
        }

        public void Dispose() { }
    }

    private sealed class StubVad(IReadOnlyList<SpeechSegment> segments) : IVoiceActivityDetector
    {
        public IReadOnlyList<SpeechSegment> Detect(float[] samples) => segments;
        public void Dispose() { }
    }

    private sealed class NoProgress : IProgress<TranscriptionProgress>
    {
        public void Report(TranscriptionProgress value) { }
    }

    private sealed class ProgressCollector(List<TranscriptionProgress> list) : IProgress<TranscriptionProgress>
    {
        public void Report(TranscriptionProgress value) => list.Add(value);
    }
}
