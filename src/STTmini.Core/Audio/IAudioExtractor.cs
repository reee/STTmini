using STTmini.Core.Audio;
using STTmini.Core.Errors;

namespace STTmini.Core.Audio;

/// <summary>
/// 音频提取接口（AGENTS.md §4.2 / §5.4）。封装 ffmpeg 进程调用。
/// 实现：将输入视频/音频一次性解码为 16kHz mono float[]（全量载入内存）。
/// </summary>
public interface IAudioExtractor
{
    /// <summary>
    /// 提取音频并返回 PCM float 样本（范围 [-1,1]，16kHz mono）。
    /// </summary>
    /// <param name="inputPath">输入文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>PCM 样本数组。</returns>
    /// <exception cref="FfmpegNotFoundException">PATH 与设置均无 ffmpeg。</exception>
    /// <exception cref="AudioExtractionException">ffmpeg 非零退出。</exception>
    Task<float[]> ExtractAsync(string inputPath, CancellationToken cancellationToken);
}
