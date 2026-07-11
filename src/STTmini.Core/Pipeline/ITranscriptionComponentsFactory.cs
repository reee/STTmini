using STTmini.Core.Audio;
using STTmini.Core.Recognition;

namespace STTmini.Core.Pipeline;

/// <summary>
/// 工厂：为每次转录运行新建原生组件（AGENTS.md §4.4 / §7）。
/// recognizer 与 VAD 均每次运行新建、运行结束释放，不跨运行共享。
/// </summary>
public interface ITranscriptionComponentsFactory
{
    /// <summary>新建识别器（调用方负责释放）。</summary>
    IRecognizer CreateRecognizer();

    /// <summary>新建 VAD（调用方负责释放）。</summary>
    IVoiceActivityDetector CreateVoiceActivityDetector();
}
