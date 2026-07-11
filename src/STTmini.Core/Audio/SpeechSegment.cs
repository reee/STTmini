namespace STTmini.Core.Audio;

/// <summary>
/// VAD 分段结果（Core 自有 DTO，由 <c>IVoiceActivityDetector</c> 实现填充）。
/// </summary>
/// <param name="StartSeconds">段在整段音频中的起点（秒）。</param>
/// <param name="Samples">段内 PCM 样本（16kHz mono float，范围 [-1,1]）。</param>
public sealed record SpeechSegment(float StartSeconds, float[] Samples)
{
    /// <summary>段时长（秒）。</summary>
    public float DurationSeconds => Samples.Length / (float)AudioConstants.SampleRate;
}
