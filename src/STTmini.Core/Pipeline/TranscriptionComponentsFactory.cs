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
    private readonly ModelPathResolver _models;
    private readonly ILoggerFactory _loggerFactory;

    public TranscriptionComponentsFactory(ModelPathResolver models, ILoggerFactory loggerFactory)
    {
        _models = models;
        _loggerFactory = loggerFactory;
    }

    public IRecognizer CreateRecognizer() =>
        new SherpaRecognizer(_models, numThreads: 1, _loggerFactory.CreateLogger<SherpaRecognizer>());

    public IVoiceActivityDetector CreateVoiceActivityDetector() =>
        new SherpaVoiceActivityDetector(_models, _loggerFactory.CreateLogger<SherpaVoiceActivityDetector>());

    /// <inheritdoc/>
    public void EnsureModelsPresent() => _models.EnsureAllPresent();
}
