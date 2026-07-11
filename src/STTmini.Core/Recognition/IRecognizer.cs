using STTmini.Core.Audio;

namespace STTmini.Core.Recognition;

/// <summary>
/// 离线识别器接口（AGENTS.md §4.2 / §4.3 / §4.4）。封装 sherpa-onnx 的 OfflineRecognizer。
/// 生命周期：每次运行新建、运行结束释放（不跨运行共享）。
/// </summary>
public interface IRecognizer : IDisposable
{
    /// <summary>
    /// 对单个语音段（或子段）执行识别。
    /// </summary>
    /// <param name="samples">段内 PCM 样本（16kHz mono float）。</param>
    /// <returns>识别结果（含 Text / Tokens / 段内相对时间戳）。</returns>
    RecognitionResult Recognize(float[] samples);
}
