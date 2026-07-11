using System.Diagnostics;
using Microsoft.Extensions.Logging;
using STTmini.Core.Configuration;
using STTmini.Core.Errors;

namespace STTmini.Core.Audio;

/// <summary>
/// <see cref="IAudioExtractor"/> 的 ffmpeg 实现（AGENTS.md §5.4 / §11.1）。
/// 一次性调用 ffmpeg 解码为 16kHz mono WAV，再全量载入为 float[]。
/// ffmpeg 路径在每次提取时按 Settings 覆盖 → PATH 顺序解析，便于运行期修正。
/// </summary>
public sealed class FfmpegAudioExtractor : IAudioExtractor
{
    private const int StderrTailLimit = 2048;

    private readonly Settings _settings;
    private readonly ILogger<FfmpegAudioExtractor> _logger;

    public FfmpegAudioExtractor(Settings settings, ILogger<FfmpegAudioExtractor> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<float[]> ExtractAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath))
        {
            throw new AudioExtractionException($"输入文件不存在：{inputPath}", "input not found");
        }

        var ffmpegPath = FfmpegLocator.Resolve(_settings.FfmpegPathOverride);

        // temp WAV 放到系统临时目录，文件名唯一。
        var tempWav = Path.Combine(Path.GetTempPath(), $"sttmini-{Guid.NewGuid():N}.wav");
        _logger.LogInformation("开始音频提取：{Input} → {Temp}", inputPath, tempWav);

        var args = FfmpegCommandBuilder.BuildArguments(inputPath, tempWav);
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = new Process { StartInfo = psi };

        if (!proc.Start())
        {
            throw new AudioExtractionException("无法启动 ffmpeg 进程", "process start failed");
        }

        // 异步读 stderr（避免管道阻塞）
        var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

        await proc.WaitForExitAsync(cancellationToken);

        var stderr = await stderrTask;
        cancellationToken.ThrowIfCancellationRequested();

        if (proc.ExitCode != 0)
        {
            TryDelete(tempWav);
            throw new AudioExtractionException(
                $"音频提取失败（ffmpeg 退出码 {proc.ExitCode}）",
                Tail(stderr));
        }

        try
        {
            var samples = WavReader.ReadMonoPcm16(tempWav);
            _logger.LogInformation(
                "音频提取完成：{N} 样本（{Sec:F1} 秒）", samples.Length, samples.Length / (float)AudioConstants.SampleRate);
            return samples;
        }
        finally
        {
            TryDelete(tempWav);
        }
    }

    private static string Tail(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
        {
            return "(无 stderr 输出)";
        }

        return stderr.Length <= StderrTailLimit
            ? stderr
            : stderr[^StderrTailLimit..];
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* temp 文件清理失败不影响主流程 */ }
    }
}
