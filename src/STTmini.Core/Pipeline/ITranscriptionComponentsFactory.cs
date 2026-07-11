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

    /// <summary>
    /// 校验全部模型文件就位（AGENTS.md §11.1）。任一缺失抛
    /// <see cref="global::STTmini.Core.Errors.ModelNotFoundException"/>，使其优先于
    /// 原生初始化异常被捕获、给出准确的 UI 提示。
    /// </summary>
    void EnsureModelsPresent();
}
