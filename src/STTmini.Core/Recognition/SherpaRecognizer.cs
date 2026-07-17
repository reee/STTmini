using Microsoft.Extensions.Logging;
using SherpaOnnx;
using STTmini.Core.Audio;
using STTmini.Core.Errors;
using STTmini.Core.Models;

namespace STTmini.Core.Recognition;

/// <summary>
/// <see cref="IRecognizer"/> 的 sherpa-onnx 实现（AGENTS.md §4.3 / §4.4）。
/// 封装 <see cref="OfflineRecognizer"/>；每次运行新建、运行结束释放。
/// </summary>
public sealed class SherpaRecognizer : IRecognizer
{
    private readonly OfflineRecognizer _recognizer;
    private readonly ILogger<SherpaRecognizer> _logger;

    public SherpaRecognizer(ModelPathResolver models, int numThreads, ILogger<SherpaRecognizer> logger)
    {
        _logger = logger;

        var config = new OfflineRecognizerConfig
        {
            FeatConfig = new FeatureConfig { SampleRate = AudioConstants.SampleRate, FeatureDim = 80 },
            ModelConfig =
            {
                Paraformer = { Model = models.ParaformerModelPath },
                Tokens = models.ParaformerTokensPath,
                NumThreads = Math.Max(1, numThreads),
                Debug = 0,
                Provider = "cpu",
            },
            DecodingMethod = "greedy_search",
        };

        try
        {
            _recognizer = new OfflineRecognizer(config);
        }
        catch (Exception ex)
        {
            throw new RecognizerInitializationException("Paraformer 识别器初始化失败", ex);
        }
    }

    /// <inheritdoc/>
    public RecognitionResult Recognize(float[] samples)
    {
        if (samples.Length == 0)
        {
            return new RecognitionResult(string.Empty, Array.Empty<string>(), Array.Empty<float>());
        }

        OfflineStream stream = _recognizer.CreateStream();
        try
        {
            stream.AcceptWaveform(AudioConstants.SampleRate, samples);
            _recognizer.Decode(stream);
            var r = stream.Result;

            return new RecognitionResult(
                Text: r.Text ?? string.Empty,
                Tokens: r.Tokens ?? Array.Empty<string>(),
                Timestamps: r.Timestamps ?? Array.Empty<float>());
        }
        catch (Exception ex)
        {
            // 记录原生异常（AGENTS.md §8.4），并向上冒泡为转录失败（AGENTS.md §11.1）：
            // 静默丢弃单段会让该段内容从字幕里消失且无任何提示。
            _logger.LogError(ex, "ASR 识别段抛出异常（samples={Count}）", samples.Length);
            throw;
        }
        finally
        {
            stream.Dispose();
        }
    }

    /// <summary>
    /// 批量识别（AGENTS.md §4.4 方案 B）：把多段打成多个 OfflineStream，
    /// 一次 <see cref="OfflineRecognizer.Decode(IEnumerable{OfflineStream})"/> 批量推理。
    /// 与逐段 <see cref="Recognize"/> 逐字一致——仅是把 N 次 native 调用合并为 1 次 batch 调用，
    /// 由 paraformer 的批维 + intra-op 线程池并行吃满多核。
    /// </summary>
    /// <remarks>
    /// 顺序保证：先按序建 stream → 按序 AcceptWaveform → 按序读取 stream.Result。
    /// sherpa-onnx 的 Decode(IEnumerable) 在 C-API 层即 <c>DecodeMultipleStreams</c>，
    /// 官方文档明确每个 stream 各自保留独立 Result，顺序不乱。
    /// </remarks>
    public IReadOnlyList<RecognitionResult> RecognizeMany(IReadOnlyList<float[]> batches)
    {
        if (batches.Count == 0)
        {
            return Array.Empty<RecognitionResult>();
        }

        // 空 samples 段单独走快速路径（与单段一致），不进 batch——
        // sherpa-onnx 对空输入行为未文档化，规避之。
        var results = new RecognitionResult[batches.Count];
        var streams = new List<OfflineStream>(batches.Count);
        var pending = new List<(int Index, OfflineStream Stream)>(batches.Count);

        try
        {
            for (int i = 0; i < batches.Count; i++)
            {
                var samples = batches[i];
                if (samples.Length == 0)
                {
                    results[i] = new RecognitionResult(string.Empty, Array.Empty<string>(), Array.Empty<float>());
                    continue;
                }

                var stream = _recognizer.CreateStream();
                stream.AcceptWaveform(AudioConstants.SampleRate, samples);
                streams.Add(stream);
                pending.Add((i, stream));
            }

            if (pending.Count > 0)
            {
                _recognizer.Decode(pending.Select(p => p.Stream));
                foreach (var (index, stream) in pending)
                {
                    var r = stream.Result;
                    results[index] = new RecognitionResult(
                        Text: r.Text ?? string.Empty,
                        Tokens: r.Tokens ?? Array.Empty<string>(),
                        Timestamps: r.Timestamps ?? Array.Empty<float>());
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ASR 批量识别抛出异常（batchSize={Count}）", batches.Count);
            throw;
        }
        finally
        {
            foreach (var s in streams)
            {
                s.Dispose();
            }
        }
    }

    public void Dispose() => _recognizer.Dispose();
}
