using Microsoft.Extensions.Logging;
using STTmini.Core.Audio;
using STTmini.Core.Models;
using STTmini.Core.Recognition;

namespace STTmini.Core.Pipeline;

/// <summary>
/// 默认工厂：每次运行新建 sherpa-onnx recognizer / VAD（AGENTS.md §4.4）。
/// </summary>
public sealed class TranscriptionComponentsFactory : ITranscriptionComponentsFactory
{
    /// <summary>
    /// ONNX Runtime intra-op 线程池大小（AGENTS.md §4.4 方案 A）。
    /// 用满物理核心；16 为防服务器核数过大的保守上限，桌面用户不受影响。
    /// </summary>
    internal const int IntraOpThreads = 16;

    private readonly ModelPathResolver _models;
    private readonly ILoggerFactory _loggerFactory;

    public TranscriptionComponentsFactory(ModelPathResolver models, ILoggerFactory loggerFactory)
    {
        _models = models;
        _loggerFactory = loggerFactory;
    }

    public IRecognizer CreateRecognizer() =>
        new SherpaRecognizer(
            _models,
            numThreads: Math.Max(1, Math.Min(Environment.ProcessorCount, IntraOpThreads)),
            _loggerFactory.CreateLogger<SherpaRecognizer>());

    public IVoiceActivityDetector CreateVoiceActivityDetector() =>
        new SherpaVoiceActivityDetector(_models, _loggerFactory.CreateLogger<SherpaVoiceActivityDetector>());

    /// <inheritdoc/>
    public void EnsureModelsPresent() => _models.EnsureAllPresent();
}
