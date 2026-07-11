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

    public void Dispose() => _recognizer.Dispose();
}
