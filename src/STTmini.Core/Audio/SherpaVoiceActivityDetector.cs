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
                // MinSilenceDuration=0.2s：与 sherpa-onnx 官方 generate-subtitles.py 一致。
                // 原值 0.5s（sherpa-onnx C++ 默认）会把中文句内停顿（中位数 ~0.47s）误判为
                // 段内静音、多句话并入一个 VAD 段，下游 PlainTextFormatter 拿不到切句信号
                // （§5.1：paraformer-zh int8 不输出标点）→ 输出一长行无切分文本。
                // 调到 0.2s 后 VAD 在「句间停顿」处切段，段间 gap 走 §5.3 的 0.6s 段落阈值。
                // 调参记录见 AGENTS.md §4.1[2] / §14.2「VAD MinSilenceDuration 调参」。
                MinSilenceDuration = 0.2f,
                MinSpeechDuration = 0.25f,
                // 与 VadWindowSlicer.WindowSize 必须一致：VAD 模型按此窗口推理。
                WindowSize = VadWindowSlicer.WindowSize,
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
            // sherpa-onnx 的 VAD 必须按窗口（VadWindowSlicer.WindowSize=512 样本）逐块喂入，
            // 否则内部 circular-buffer 会溢出、语音状态被破坏——实测一次性喂入 672s 音频
            // 只剩末尾 0.3s 一段。官方 C# 示例（vad-non-streaming-asr-paraformer）即按
            // WindowSize 循环 AcceptWaveform。切片逻辑见 VadWindowSlicer（AGENTS.md §4.2）。
            foreach (var (offset, length) in VadWindowSlicer.Slice(samples.Length))
            {
                // 每个窗口复制成独立短缓冲：满窗可复用，尾部短窗按实际长度分配。
                var window = new float[length];
                Array.Copy(samples, offset, window, 0, length);
                _vad.AcceptWaveform(window);
                DrainCompleted(segments);
            }

            _vad.Flush();
            DrainCompleted(segments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VAD 处理抛出异常（samples={Count}），已返回 {N} 段", samples.Length, segments.Count);
        }

        return segments;
    }

    /// <summary>
    /// 弹出 VAD 已完成检测的段，换算 <c>Start</c> 样本偏移为秒后加入结果。
    /// </summary>
    private void DrainCompleted(List<SpeechSegment> segments)
    {
        while (!_vad.IsEmpty())
        {
            var seg = _vad.Front();
            // sherpa-onnx 的 SpeechSegment.Start 是样本偏移（int），换算为秒。
            float startSeconds = seg.Start / (float)AudioConstants.SampleRate;
            segments.Add(new SpeechSegment(startSeconds, seg.Samples));
            _vad.Pop();
        }
    }

    public void Dispose() => _vad.Dispose();
}
