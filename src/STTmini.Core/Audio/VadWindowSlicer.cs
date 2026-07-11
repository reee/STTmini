namespace STTmini.Core.Audio;

/// <summary>
/// 把整段音频按 Silero VAD 的窗口大小切成若干喂入切片（纯逻辑、可测试）。
/// sherpa-onnx 的 <c>VoiceActivityDetector.AcceptWaveform</c> 是流式 API，必须逐块喂入；
/// 一次性喂入整段音频会破坏内部状态（circular-buffer 溢出后仅保留尾部）。
/// 见 AGENTS.md §4.1[2] / §4.2。
/// </summary>
public static class VadWindowSlicer
{
    /// <summary>
    /// Silero VAD 的分析窗口大小（样本数）。与 <c>VadModelConfig.SileroVad.WindowSize</c> 一致，
    /// 必须同步——VAD 模型按此窗口进行推理。
    /// </summary>
    public const int WindowSize = 512;

    /// <summary>
    /// 把长度为 <paramref name="totalSamples"/> 的音频切成若干 (Offset, Length) 窗口，
    /// 供逐块 <c>AcceptWaveform</c>。窗口连续、不重叠、完整覆盖输入；末窗可能短于
    /// <see cref="WindowSize"/>（尾部余量）。空输入返回空序列。
    /// </summary>
    /// <param name="totalSamples">待切分的样本总数。</param>
    /// <returns>窗口切片，每项为 (在整段音频中的起点偏移, 该窗口长度)。</returns>
    public static IEnumerable<(int Offset, int Length)> Slice(int totalSamples)
    {
        if (totalSamples <= 0)
        {
            yield break;
        }

        int offset = 0;
        while (offset < totalSamples)
        {
            int length = Math.Min(WindowSize, totalSamples - offset);
            yield return (offset, length);
            offset += length;
        }
    }
}
