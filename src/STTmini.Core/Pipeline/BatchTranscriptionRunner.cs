using System.IO;
using Microsoft.Extensions.Logging;
using STTmini.Core.Errors;
using STTmini.Core.Recognition;
using STTmini.Core.Subtitles;

namespace STTmini.Core.Pipeline;

/// <summary>
/// 批量输出写入抽象。把"把文本写到磁盘"这一副作用隔离出来，
/// 让 <see cref="BatchTranscriptionRunner"/> 可在测试中替换为内存采集器（AGENTS.md §4.3 seam）。
/// </summary>
public interface IBatchOutputWriter
{
    /// <summary>把 <paramref name="content"/> 写到 <paramref name="path"/>（覆盖已存在文件）。</summary>
    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IBatchOutputWriter"/> 的默认实现：直接 <see cref="File.WriteAllTextAsync(string, string, System.Threading.CancellationToken)"/>。
/// </summary>
public sealed class FileBatchOutputWriter : IBatchOutputWriter
{
    /// <summary>默认单例（无状态）。</summary>
    public static FileBatchOutputWriter Instance { get; } = new();

    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }
}

/// <summary>
/// 批量转录编排器（AGENTS.md §4.5）。顺序调用 <see cref="ITranscriptionEngine.TranscribeAsync"/> N 次，
/// 失败跳过继续，按 <see cref="BatchOutputFormat"/> 标志位写输出文件，进度两层上抛（文件 i/N 包段 j/M）。
/// </summary>
/// <remarks>
/// 不做应用层并行（AGENTS.md §4.4 明确否决：recognizer 非线程安全 + 内存 ×N + 线程过订阅）。
/// 每个文件复用注入的 <see cref="ITranscriptionEngine"/>（其内部 per-call 新建/释放 recognizer）。
/// 单文件内仍吃满 intra-op 多线程 + 原生 batch decode。
/// </remarks>
public sealed class BatchTranscriptionRunner
{
    private readonly ITranscriptionEngine _engine;
    private readonly IBatchOutputWriter _outputWriter;
    private readonly ILogger<BatchTranscriptionRunner> _logger;

    public BatchTranscriptionRunner(
        ITranscriptionEngine engine,
        IBatchOutputWriter outputWriter,
        ILogger<BatchTranscriptionRunner> logger)
    {
        _engine = engine;
        _outputWriter = outputWriter;
        _logger = logger;
    }

    /// <summary>
    /// 执行一次批量转录。
    /// </summary>
    /// <param name="inputPaths">输入文件列表（已展开/去重，通常来自 <c>BatchInputCollector.Collect</c>）。</param>
    /// <param name="formats">输出格式标志位（至少一个，None 时抛 <see cref="ArgumentException"/>）。</param>
    /// <param name="progress">外层进度回传（文件 i/N + 当前文件段进度 + 文件完成事件）。</param>
    /// <param name="cancellationToken">批量取消令牌（每个文件边界检查）。</param>
    /// <returns>各文件结局汇总。</returns>
    public async Task<BatchTranscriptionResult> RunAsync(
        IReadOnlyList<string> inputPaths,
        BatchOutputFormat formats,
        IProgress<BatchTranscriptionProgress> progress,
        CancellationToken cancellationToken)
    {
        if (formats == BatchOutputFormat.None)
        {
            throw new ArgumentException("至少选择一种输出格式。", nameof(formats));
        }
        if (inputPaths.Count == 0)
        {
            // 防御：VM 已守此前置条件（CanStartBatch），但 runner 是公共编排入口，
            // 不应静默返回 0/0 汇总——与 formats==None 同等对待为契约违反。
            throw new ArgumentException("批量输入列表为空。", nameof(inputPaths));
        }

        var outcomes = new List<BatchFileOutcome>(inputPaths.Count);
        int total = inputPaths.Count;

        for (int i = 0; i < inputPaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string inputPath = inputPaths[i];
            string fileName = Path.GetFileName(inputPath);

            // 内层进度转译：把单文件 TranscriptionProgress 包装成带文件上下文的 BatchTranscriptionProgress。
            // 每个文件起一个新 Progress<T>（捕获 UI 线程 SynchronizationContext 由调用方在构造外层 progress 时完成）。
            var inner = new Progress<TranscriptionProgress>(p =>
            {
                progress.Report(new BatchTranscriptionProgress(
                    CurrentFileIndex: i + 1,
                    TotalFiles: total,
                    CurrentFileName: fileName,
                    Stage: p.Stage,
                    CurrentSegment: p.CurrentSegment,
                    TotalSegments: p.TotalSegments,
                    JustCompleted: null));
            });

            try
            {
                var result = await _engine.TranscribeAsync(inputPath, inner, cancellationToken).ConfigureAwait(false);

                var outputs = new List<string>(2);
                if ((formats & BatchOutputFormat.Txt) != 0)
                {
                    string txtPath = BatchOutputResolver.ResolveOutputPath(inputPath, "txt");
                    await _outputWriter.WriteTextAsync(txtPath, PlainTextFormatter.Format(result.Segments), cancellationToken).ConfigureAwait(false);
                    outputs.Add(txtPath);
                }
                if ((formats & BatchOutputFormat.Srt) != 0)
                {
                    string srtPath = BatchOutputResolver.ResolveOutputPath(inputPath, "srt");
                    await _outputWriter.WriteTextAsync(srtPath, SrtFormatter.Format(result.Segments), cancellationToken).ConfigureAwait(false);
                    outputs.Add(srtPath);
                }

                var outcome = BatchFileOutcome.Succeeded(inputPath, outputs);
                outcomes.Add(outcome);
                progress.Report(new BatchTranscriptionProgress(
                    CurrentFileIndex: i + 1,
                    TotalFiles: total,
                    CurrentFileName: fileName,
                    Stage: TranscriptionStage.Done,
                    CurrentSegment: 0,
                    TotalSegments: 0,
                    JustCompleted: outcome));
            }
            catch (OperationCanceledException)
            {
                // 取消向上冒泡：已完成的 outcome 保留，不再处理后续文件。
                _logger.LogInformation("批量转录在文件 {Index}/{Total}（{File}）处被取消", i + 1, total, fileName);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量转录：文件 {File} 失败，跳过继续", fileName);
                var outcome = BatchFileOutcome.Failed(inputPath, FriendlyError(ex));
                outcomes.Add(outcome);
                progress.Report(new BatchTranscriptionProgress(
                    CurrentFileIndex: i + 1,
                    TotalFiles: total,
                    CurrentFileName: fileName,
                    Stage: TranscriptionStage.Failed,
                    CurrentSegment: 0,
                    TotalSegments: 0,
                    JustCompleted: outcome));
            }
        }

        return new BatchTranscriptionResult(outcomes);
    }

    /// <summary>
    /// 把异常映射为适合 UI 列表行显示的简短文案。与 MainWindowViewModel 单文件路径的提示口径一致（AGENTS.md §11.1）。
    /// 这些文案走批量列表行（一行展示），故意比单文件 toast 更短。
    /// </summary>
    private static string FriendlyError(Exception ex) => ex switch
    {
        FfmpegNotFoundException => "未找到 ffmpeg",
        AudioExtractionException ae => $"音频提取失败：{ae.StderrTail}",
        ModelNotFoundException => "模型文件缺失",
        RecognizerInitializationException => "识别引擎初始化失败",
        _ => ex.Message,
    };
}
