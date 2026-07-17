namespace STTmini.Core.Pipeline;

/// <summary>
/// 转录引擎抽象（AGENTS.md §4.3 接口隔离原则）。
/// 抽出的目的：让批量编排 <see cref="BatchTranscriptionRunner"/> 可独立于真实引擎测试，
/// 与 <c>IRecognizer</c> / <c>IAudioExtractor</c> / <c>IVoiceActivityDetector</c> 等同属一道 seam。
/// </summary>
public interface ITranscriptionEngine
{
    /// <summary>
    /// 执行一次完整转录（单文件）。批量模式由 <see cref="BatchTranscriptionRunner"/> 顺序调用本方法 N 次。
    /// </summary>
    /// <param name="inputPath">输入视频/音频文件。</param>
    /// <param name="progress">进度回传（UI 线程 marshal 由调用方/框架处理）。</param>
    /// <param name="cancellationToken">取消令牌（批边界生效，§4.4 / §6.4）。</param>
    /// <returns>转录结果（按段识别结果 + 纯文本预览）。其它格式由调用方按 <see cref="TranscriptionResult.Segments"/> 即时格式化。</returns>
    Task<TranscriptionResult> TranscribeAsync(
        string inputPath,
        IProgress<TranscriptionProgress> progress,
        CancellationToken cancellationToken);
}
