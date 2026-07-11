namespace STTmini.Core.Audio;

/// <summary>
/// 构造 ffmpeg 解码命令（AGENTS.md §5.4）。纯逻辑、可测试。
/// 约定：一次性调用，产出 16kHz mono WAV。
/// </summary>
public static class FfmpegCommandBuilder
{
    public const int SampleRate = AudioConstants.SampleRate;

    /// <summary>
    /// 构造 ffmpeg 参数列表（不含可执行文件名本身）。
    /// 形如：-i &lt;input&gt; -ar 16000 -ac 1 -f wav -y &lt;output&gt;
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(string inputPath, string outputPath)
    {
        return
        [
            "-y",              // 覆盖输出
            "-i", inputPath,   // 输入
            "-vn",             // 忽略视频流
            "-ar", SampleRate.ToString(), // 采样率
            "-ac", "1",        // 单声道
            "-f", "wav",       // PCM WAV
            outputPath,        // 输出
        ];
    }
}
