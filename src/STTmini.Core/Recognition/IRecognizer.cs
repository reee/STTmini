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

    /// <summary>
    /// 批量识别多段（AGENTS.md §4.4 方案 B：原生 batch 解码，吞吐优化）。
    /// 结果顺序与 <paramref name="batches"/> 一致。
    /// 默认实现回退为逐段 <see cref="Recognize"/>，供测试 stub 继承——
    /// 不覆盖即等价于串行循环，保持现有可测性。
    /// </summary>
    /// <param name="batches">每段 PCM 样本，顺序即结果顺序。</param>
    /// <returns>每段识别结果（与输入同序）。</returns>
    IReadOnlyList<RecognitionResult> RecognizeMany(IReadOnlyList<float[]> batches)
        => batches.Select(Recognize).ToList();
}
