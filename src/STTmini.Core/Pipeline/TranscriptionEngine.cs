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
    /// <summary>
    /// ASR 批大小（AGENTS.md §4.1[4] / §4.4 方案 B）。每批段合并为一次
    /// <c>OfflineRecognizer.Decode(IEnumerable&lt;OfflineStream&gt;)</c>，由 paraformer 批维 +
    /// intra-op 线程池并行吃满多核。8 是吞吐与 padding 浪费的折衷（VAD 段长天然相近）。
    /// public 供测试引用，避免生产/测试各定义一份魔法数。
    /// </summary>
    public const int BatchSize = 8;

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
    /// <param name="cancellationToken">取消令牌（批边界生效，§4.4 / §6.4）。</param>
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

            // [4] 分批 batch 识别（AGENTS.md §4.1[4] / §4.4 方案 B）：
            // 按 BatchSize 段成批送入 OfflineRecognizer.Decode(IEnumerable<OfflineStream>)，
            // 由 paraformer 批维 + intra-op 线程池并行吃满多核。
            // 批结果按 stream 创建顺序读取，段顺序、时间戳、纯文本分隔（§5.3）全部保持。
            float previousSegmentEnd = 0f;
            int index = 0;
            for (int batchStart = 0; batchStart < chunked.Count; batchStart += BatchSize)
            {
                // 取消在批边界生效（AGENTS.md §6.4）。
                cancellationToken.ThrowIfCancellationRequested();

                int batchLength = Math.Min(BatchSize, chunked.Count - batchStart);
                var batchSamples = new List<float[]>(batchLength);
                for (int i = batchStart; i < batchStart + batchLength; i++)
                {
                    batchSamples.Add(chunked[i].Samples);
                }

                var batchResults = recognizer.RecognizeMany(batchSamples);

                // 按批内顺序逐段：时间戳修正 + 元信息 + 实时进度回传（§6.3）。
                for (int j = 0; j < batchLength; j++)
                {
                    index++;
                    var chunk = chunked[batchStart + j];
                    var result = batchResults[j];

                    progress.Report(new TranscriptionProgress(
                        TranscriptionStage.Recognizing,
                        TranscriptionProgress.LabelFor(TranscriptionStage.Recognizing, index, chunked.Count),
                        index,
                        chunked.Count));

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
