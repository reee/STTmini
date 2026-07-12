using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using STTmini.App.Services;
using STTmini.Core.Audio;
using STTmini.Core.Configuration;
using STTmini.Core.Errors;
using STTmini.Core.Pipeline;
using STTmini.Core.Subtitles;

namespace STTmini.App.ViewModels;

/// <summary>
/// 主窗口 ViewModel（AGENTS.md §6.2 / §6.3 / §6.4 / §7）。
/// 单文件工作流：选输入 → 转录（进度+可取消）→ 查看纯文本结果 → 保存文本 / 保存字幕。
/// 两种格式从同一份段数据即时格式化，无需重跑识别（§6.2）。
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly TranscriptionEngine _engine;
    private readonly IFilePickerService _filePicker;
    private readonly Settings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly ILogger<MainWindowViewModel> _logger;

    private CancellationTokenSource? _cts;
    private string? _inputPath;

    /// <summary>最近一次转录的按段结果（供「保存文本 / 保存字幕」即时格式化，AGENTS.md §6.2）。</summary>
    private IReadOnlyList<Core.Recognition.SegmentRecognition>? _lastSegments;

    /// <summary>实时填充用的纯文本缓冲区（与 <see cref="PlainTextFormatter"/> 共用规则）。</summary>
    private readonly StringBuilder _liveBuffer = new();

    public MainWindowViewModel(
        TranscriptionEngine engine,
        IFilePickerService filePicker,
        Settings settings,
        SettingsStore settingsStore,
        SettingsViewModel settingsPage,
        ILogger<MainWindowViewModel> logger)
    {
        _engine = engine;
        _filePicker = filePicker;
        _settings = settings;
        _settingsStore = settingsStore;
        SettingsPage = settingsPage;
        _logger = logger;
    }

    /// <summary>输入文件路径（只读显示）。</summary>
    [ObservableProperty]
    private string _inputFilePath = string.Empty;

    /// <summary>转录输出文本。</summary>
    [ObservableProperty]
    private string _outputText = string.Empty;

    /// <summary>当前进度标签（AGENTS.md §6.3）。</summary>
    [ObservableProperty]
    private string _progressLabel = string.Empty;

    /// <summary>进度值 0~1。</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>是否正在转录。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranscribeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveTextCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSubtitleCommand))]
    private bool _isBusy;

    /// <summary>错误/提示消息。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>设置页 VM（嵌入主窗口的设置区）。</summary>
    public SettingsViewModel SettingsPage { get; }

    /// <summary>选择输入文件。</summary>
    [RelayCommand]
    private async Task PickInputFileAsync()
    {
        var path = await _filePicker.PickOpenFileAsync(
            "选择视频或音频文件",
            "*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm", "*.mp3", "*.wav", "*.m4a", "*.flac", "*.aac");

        if (path is null)
        {
            return;
        }

        SetInputPath(path);
    }

    /// <summary>
    /// 拖放落点：由 view 层的 DragDrop 事件转发文件路径调用（AGENTS.md §6.2：支持拖放）。
    /// 传 null 表示拖放被取消/无有效文件，直接忽略。
    /// </summary>
    public void AcceptDroppedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        SetInputPath(path);
    }

    private void SetInputPath(string path)
    {
        _inputPath = path;
        InputFilePath = path;
        _settings.LastInputDirectory = Path.GetDirectoryName(path);
        try { _settingsStore.Save(_settings); } catch { /* 非关键 */ }

        OutputText = string.Empty;
        _lastSegments = null;
        StatusMessage = string.Empty;
        // 清空结果后刷新保存按钮的可用态（否则上一轮的段数据会让按钮残留启用）。
        SaveTextCommand.NotifyCanExecuteChanged();
        SaveSubtitleCommand.NotifyCanExecuteChanged();
    }

    /// <summary>开始转录。</summary>
    [RelayCommand(CanExecute = nameof(CanTranscribe))]
    private async Task TranscribeAsync()
    {
        if (string.IsNullOrEmpty(_inputPath))
        {
            StatusMessage = "请先选择输入文件。";
            return;
        }

        IsBusy = true;
        Progress = 0;
        ProgressLabel = string.Empty;
        OutputText = string.Empty;
        StatusMessage = string.Empty;
        _lastSegments = null;
        _liveBuffer.Clear();
        _cts = new CancellationTokenSource();

        var progress = new Progress<TranscriptionProgress>(OnProgress);

        try
        {
            // 引擎只产纯文本预览（UI 主显示）；SRT 由「保存字幕」按钮按 Segments 即时格式化写出（AGENTS.md §6.2）。
            var result = await Task.Run(() => _engine.TranscribeAsync(_inputPath, progress, _cts.Token), _cts.Token);
            _lastSegments = result.Segments;
            OutputText = result.PlainText;
            StatusMessage = "转录完成。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消。";
        }
        catch (FfmpegNotFoundException ex)
        {
            _logger.LogError(ex, "未找到 ffmpeg");
            StatusMessage = "未找到 ffmpeg，请在「设置」中配置路径。";
        }
        catch (AudioExtractionException ex)
        {
            _logger.LogError(ex, "音频提取失败");
            StatusMessage = $"音频提取失败：{ex.StderrTail}";
        }
        catch (ModelNotFoundException ex)
        {
            _logger.LogError(ex, "模型文件缺失：{Path}", ex.MissingPath);
            StatusMessage = "模型文件缺失，请重新安装或检查程序目录。";
        }
        catch (RecognizerInitializationException ex)
        {
            _logger.LogError(ex, "识别引擎初始化失败");
            StatusMessage = "识别引擎初始化失败，详见日志。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "转录发生未预期错误");
            StatusMessage = $"发生错误：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanTranscribe() => !IsBusy;

    /// <summary>取消转录（段边界生效，AGENTS.md §6.4）。</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = "正在取消…";
    }

    private bool CanCancel() => IsBusy && _cts is not null;

    /// <summary>保存纯文本（.txt）。AGENTS.md §6.2：两种格式都从同一份段数据即时格式化。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveTextAsync() => SaveAsync(SaveProfile.PlainText);

    /// <summary>保存 SRT 字幕（.srt）。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveSubtitleAsync() => SaveAsync(SaveProfile.Srt);

    /// <summary>
    /// 两种保存的公共实现：按 <paramref name="profile"/> 即时格式化 <see cref="_lastSegments"/>，
    /// 弹保存对话框并写盘。无段数据时直接忽略（按钮本身已禁用，此为兜底）。
    /// </summary>
    private async Task SaveAsync(SaveProfile profile)
    {
        if (_lastSegments is not { Count: > 0 } segments)
        {
            return;
        }

        var baseName = !string.IsNullOrEmpty(_inputPath)
            ? Path.GetFileNameWithoutExtension(_inputPath)
            : "transcript";

        var path = await _filePicker.PickSaveFileAsync(
            profile.DialogTitle, $"{baseName}.{profile.Extension}", profile.Extension);

        if (path is null)
        {
            return;
        }

        try
        {
            var text = profile.Format(segments);
            await _filePicker.SaveTextAsync(path, text);
            StatusMessage = $"已保存：{path}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存失败");
            StatusMessage = $"保存失败：{ex.Message}";
        }
    }

    /// <summary>有可保存的结果：非忙碌且有段数据（字幕必须有时间戳，纯文本不够）。</summary>
    private bool CanSave() => !IsBusy && _lastSegments is { Count: > 0 };

    /// <summary>
    /// 进度回调。<see cref="Progress{T}"/> 已捕获 UI 线程的 SynchronizationContext，
    /// 回调本身即在 UI 线程触发，无需再 marshal（AGENTS.md §7）。
    /// </summary>
    private void OnProgress(TranscriptionProgress p)
    {
        ProgressLabel = p.Label;
        if (p.TotalSegments > 0 && p.Stage == TranscriptionStage.Recognizing)
        {
            Progress = (double)p.CurrentSegment / p.TotalSegments;
        }
        else if (p.Stage == TranscriptionStage.Done)
        {
            Progress = 1;
        }

        // 实时填充（AGENTS.md §6.3）：复用 PlainTextFormatter 的分隔规则，避免漂移。
        if (p.LatestSegment is not null)
        {
            PlainTextFormatter.AppendSegment(_liveBuffer, p.LatestSegment, isFirst: _liveBuffer.Length == 0);
            OutputText = _liveBuffer.ToString();
        }
    }

    /// <summary>
    /// 单个保存目标（文件扩展名 / 对话框标题 / 段→文本格式化器）的描述。
    /// 把"按格式分叉"的三处知识（扩展名、标题、格式化）收拢到一处，避免散落的 switch（§6.2 双保存）。
    /// </summary>
    private sealed class SaveProfile(string extension, string dialogTitle, Func<IReadOnlyList<Core.Recognition.SegmentRecognition>, string> format)
    {
        public static SaveProfile PlainText { get; } = new("txt", "保存文本", PlainTextFormatter.Format);
        public static SaveProfile Srt { get; } = new("srt", "保存字幕", SrtFormatter.Format);

        public string Extension { get; } = extension;
        public string DialogTitle { get; } = dialogTitle;
        public string Format(IReadOnlyList<Core.Recognition.SegmentRecognition> segments) => format(segments);
    }
}
