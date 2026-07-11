using Microsoft.Extensions.Logging;
using SherpaOnnx;
using STTmini.Core.Errors;
using STTmini.Core.Models;

namespace STTmini.Core.Audio;

/// <summary>
/// <see cref="IVoiceActivityDetector"/> 的 sherpa-onnx Silero VAD 实现
/// （AGENTS.md §4.1[2] / §4.3）。
/// </summary>
public sealed class SherpaVoiceActivityDetector : IVoiceActivityDetector
{
    private readonly VoiceActivityDetector _vad;
    private readonly ILogger<SherpaVoiceActivityDetector> _logger;

    public SherpaVoiceActivityDetector(ModelPathResolver models, ILogger<SherpaVoiceActivityDetector> logger)
    {
        _logger = logger;

        var config = new VadModelConfig
        {
            SileroVad =
            {
                Model = models.SileroVadPath,
                Threshold = 0.5f,
                // 默认 MinSilenceDuration=0.5s、MinSpeechDuration=0.25s 合理，沿用。
                MinSilenceDuration = 0.5f,
                MinSpeechDuration = 0.25f,
                WindowSize = 512,
                // 默认值 5.0s 会让 VAD 自行把长句切成 ≤5s 段；
                // v1 统一由 SegmentChunker 的 25s 窗口切分（AGENTS.md §4.1[3]），
                // 故抬高到 30s，使 VAD 输出的段尽可能完整。
                MaxSpeechDuration = 30f,
            },
            SampleRate = AudioConstants.SampleRate,
            NumThreads = 1,
            Provider = "cpu",
        };

        try
        {
            // 缓冲 60 秒音频；超长输入会自动扩容。
            _vad = new VoiceActivityDetector(config, bufferSizeInSeconds: 60f);
        }
        catch (Exception ex)
        {
            throw new RecognizerInitializationException("VAD 初始化失败", ex);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<SpeechSegment> Detect(float[] samples)
    {
        if (samples.Length == 0)
        {
            return Array.Empty<SpeechSegment>();
        }

        var segments = new List<SpeechSegment>();
        try
        {
            _vad.AcceptWaveform(samples);
            _vad.Flush();

            while (!_vad.IsEmpty())
            {
                var seg = _vad.Front();
                // sherpa-onnx 的 SpeechSegment.Start 是样本偏移（int），换算为秒。
                float startSeconds = seg.Start / (float)AudioConstants.SampleRate;
                segments.Add(new SpeechSegment(startSeconds, seg.Samples));
                _vad.Pop();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VAD 处理抛出异常（samples={Count}），已返回 {N} 段", samples.Length, segments.Count);
        }

        return segments;
    }

    public void Dispose() => _vad.Dispose();
}
