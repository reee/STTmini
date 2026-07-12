using Microsoft.Extensions.Logging;
using STTmini.Core.Audio;
using STTmini.Core.Recognition;
using STTmini.Core.Subtitles;

namespace STTmini.Core.Pipeline;

/// <summary>
/// 转录引擎：编排 AGENTS.md §4.1 的整条流水线（AGENTS.md §7）。
/// 单 worker per run：每次 <see cref="TranscribeAsync"/> 调用起一个后台 Task，串行处理。
/// </summary>
public sealed class TranscriptionEngine
{
    private readonly IAudioExtractor _audioExtractor;
    private readonly ITranscriptionComponentsFactory _components;
    private readonly ILogger<TranscriptionEngine> _logger;

    public TranscriptionEngine(
        IAudioExtractor audioExtractor,
        ITranscriptionComponentsFactory components,
        ILogger<TranscriptionEngine> logger)
    {
        _audioExtractor = audioExtractor;
        _components = components;
        _logger = logger;
    }

    /// <summary>
    /// 执行一次完整转录。
    /// </summary>
    /// <param name="inputPath">输入视频/音频文件。</param>
    /// <param name="progress">进度回传（UI 线程 marshal 由调用方/框架处理）。</param>
    /// <param name="cancellationToken">取消令牌（段边界生效）。</param>
    /// <returns>转录结果（按段识别结果 + 纯文本预览）。SRT 等其它格式由调用方按 <see cref="TranscriptionResult.Segments"/> 即时格式化。</returns>
    public async Task<TranscriptionResult> TranscribeAsync(
        string inputPath,
        IProgress<TranscriptionProgress> progress,
        CancellationToken cancellationToken)
    {
        // 模型存在性校验优先于一切原生初始化（AGENTS.md §11.1）：
        // 缺模型时抛 ModelNotFoundException，而非下游的 RecognizerInitializationException。
        _components.EnsureModelsPresent();

        // [1] ffmpeg 解码（AGENTS.md §4.1[1] / §5.4）
        progress.Report(Stage(TranscriptionStage.DecodingAudio));
        float[] samples = await _audioExtractor.ExtractAsync(inputPath, cancellationToken);
        _logger.LogInformation("音频解码完成：{N} 样本", samples.Length);

        // [2] VAD 分段（AGENTS.md §4.1[2]）
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(Stage(TranscriptionStage.VoiceActivityDetection));

        var recognized = new List<SegmentRecognition>();
        using (var vad = _components.CreateVoiceActivityDetector())
        {
            var segments = vad.Detect(samples);
            _logger.LogInformation("VAD 输出 {N} 段", segments.Count);

            // [3] 超长段重切 + [4] ASR 识别（AGENTS.md §4.1[3]/[4]）
            // 先把所有段重切展开，得到子段总数用于进度。
            var chunked = new List<ChunkedSegment>();
            foreach (var seg in segments)
            {
                chunked.AddRange(SegmentChunker.Chunk(seg));
            }

            using var recognizer = _components.CreateRecognizer();

            float previousSegmentEnd = 0f;
            int index = 0;
            foreach (var chunk in chunked)
            {
                // 取消在段边界生效（AGENTS.md §6.4）
                cancellationToken.ThrowIfCancellationRequested();
                index++;

                progress.Report(new TranscriptionProgress(
                    TranscriptionStage.Recognizing,
                    TranscriptionProgress.LabelFor(TranscriptionStage.Recognizing, index, chunked.Count),
                    index,
                    chunked.Count));

                var result = recognizer.Recognize(chunk.Samples);

                // [5] 时间戳修正 + 段元信息（AGENTS.md §4.1[5] / §5.1）
                float silenceBefore = Math.Max(0f, chunk.GlobalStartSeconds - previousSegmentEnd);
                var segRecognition = new SegmentRecognition(
                    GlobalStartSeconds: chunk.GlobalStartSeconds,
                    GlobalEndSeconds: chunk.GlobalEndSeconds,
                    Result: result,
                    SilenceBeforeSeconds: silenceBefore);

                recognized.Add(segRecognition);
                previousSegmentEnd = Math.Max(previousSegmentEnd, chunk.GlobalEndSeconds);

                // [6] 实时填充（AGENTS.md §6.3）
                progress.Report(new TranscriptionProgress(
                    TranscriptionStage.Recognizing,
                    TranscriptionProgress.LabelFor(TranscriptionStage.Recognizing, index, chunked.Count),
                    index,
                    chunked.Count,
                    LatestSegment: segRecognition));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // [6] 输出格式化（AGENTS.md §4.1[6] / §5.3）：纯文本预览。
        // 引擎只产纯文本（UI 主显示）；SRT 由调用方按 Segments 即时格式化（§6.2 双保存）。
        progress.Report(Stage(TranscriptionStage.Formatting));
        var text = PlainTextFormatter.Format(recognized);

        progress.Report(Stage(TranscriptionStage.Done));
        return new TranscriptionResult(recognized, text);
    }

    private static TranscriptionProgress Stage(TranscriptionStage stage)
        => new(stage, TranscriptionProgress.LabelFor(stage), 0, 0);
}
