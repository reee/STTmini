namespace STTmini.Core.Audio;

/// <summary>
/// 语音活动检测接口（AGENTS.md §4.2 / §4.3）。封装 sherpa-onnx 的 VoiceActivityDetector。
/// 实现须线程安全（单 worker per run 内使用，但保留接口中立性）。
/// </summary>
public interface IVoiceActivityDetector : IDisposable
{
    /// <summary>
    /// 对整段音频做 VAD，返回按时间顺序的语音段。
    /// </summary>
    /// <param name="samples">整段音频 PCM 样本（16kHz mono float）。</param>
    /// <returns>语音段列表（每段含全局起点与样本）。</returns>
    IReadOnlyList<SpeechSegment> Detect(float[] samples);
}
