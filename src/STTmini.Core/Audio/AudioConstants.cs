namespace STTmini.Core.Audio;

/// <summary>
/// 全局音频常量。整条流水线约定 16kHz mono PCM（AGENTS.md §5.4）。
/// </summary>
public static class AudioConstants
{
    /// <summary>目标采样率（Hz）。</summary>
    public const int SampleRate = 16000;
}
