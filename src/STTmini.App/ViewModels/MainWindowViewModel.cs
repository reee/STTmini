using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private readonly ITranscriptionEngine _engine;
    private readonly BatchTranscriptionRunner _batchRunner;
    private readonly IFilePickerService _filePicker;
    private readonly IFileLauncher _fileLauncher;
    private readonly Settings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly ILogger<MainWindowViewModel> _logger;

    private CancellationTokenSource? _cts;
    private string? _inputPath;

    /// <summary>批量模式专属取消令牌（与单文件 <see cref="_cts"/> 隔离，AGENTS.md §6.4）。</summary>
    private CancellationTokenSource? _batchCts;

    /// <summary>最近一次转录的按段结果（供「保存文本 / 保存字幕」即时格式化，AGENTS.md §6.2）。</summary>
    private IReadOnlyList<Core.Recognition.SegmentRecognition>? _lastSegments;

    /// <summary>实时填充用的纯文本缓冲区（与 <see cref="PlainTextFormatter"/> 共用规则）。</summary>
    private readonly StringBuilder _liveBuffer = new();

    public MainWindowViewModel(
        ITranscriptionEngine engine,
        BatchTranscriptionRunner batchRunner,
        IFilePickerService filePicker,
        IFileLauncher fileLauncher,
        Settings settings,
        SettingsStore settingsStore,
        SettingsViewModel settingsPage,
        ILogger<MainWindowViewModel> logger)
    {
        _engine = engine;
        _batchRunner = batchRunner;
        _filePicker = filePicker;
        _fileLauncher = fileLauncher;
        _settings = settings;
        _settingsStore = settingsStore;
        SettingsPage = settingsPage;
        _logger = logger;

        // 列表项增删 → 联动刷新计数 / 「清空已完成」可用态 / 各批量命令可用态。
        BatchItems.CollectionChanged += OnBatchItemsChanged;

        // 启动即检测 ffmpeg：不可用时 CTA 灰显 + StatusMessage 写提示，用户一眼知道下一步。
        RefreshFfmpegStatus();
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
    [NotifyCanExecuteChangedFor(nameof(SaveTextCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSubtitleCommand))]
    private bool _isBusy;

    /// <summary>错误/提示消息。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>ffmpeg 检测后的实际解析路径（空 = 未找到）。状态变化时联动刷新 CTA 可用态。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranscribeCommand))]
    private string _ffmpegResolvedPath = string.Empty;

    /// <summary>ffmpeg 是否可用（解析路径非空）。CTA 禁用态的判据之一。</summary>
    public bool IsFfmpegAvailable => !string.IsNullOrEmpty(FfmpegResolvedPath);

    /// <summary>ffmpeg 不可用时写进 StatusMessage 的空闲态提示。</summary>
    private const string FfmpegMissingHint = "未检测到 ffmpeg，点右上角 ⚙ 设置路径";

    /// <summary>FfmpegResolvedPath 变化时，派生属性 IsFfmpegAvailable 也要通知绑定刷新。</summary>
    partial void OnFfmpegResolvedPathChanged(string value)
        => OnPropertyChanged(nameof(IsFfmpegAvailable));

    /// <summary>
    /// 重新检测 ffmpeg 可用性（PATH 或 Settings 覆盖路径）。
    /// 设置弹窗关闭后由 view 层调用——Settings 是 DI 单例，此时已拿到最新 FfmpegPathOverride。
    /// 走 <see cref="FfmpegLocator.TryResolve"/> 不抛版，缺 ffmpeg 只置空状态 + 提示，
    /// 转录入口靠 CanTranscribe 兜住。
    /// </summary>
    public void RefreshFfmpegStatus()
    {
        FfmpegResolvedPath = FfmpegLocator.TryResolve(_settings.FfmpegPathOverride).Path ?? string.Empty;
        UpdateIdleStatusHint();
    }

    /// <summary>
    /// 按 ffmpeg 可用性刷新空闲态提示：不可用 → 写阻碍提示；
    /// 可用且当前正显示该提示 → 清掉（转录中/完成态的不动）。
    /// </summary>
    private void UpdateIdleStatusHint()
    {
        if (IsBusy)
        {
            return;
        }

        if (!IsFfmpegAvailable)
        {
            StatusMessage = FfmpegMissingHint;
        }
        else if (StatusMessage == FfmpegMissingHint)
        {
            StatusMessage = string.Empty;
        }
    }

    /// <summary>设置页 VM（嵌入主窗口的设置区）。</summary>
    public SettingsViewModel SettingsPage { get; }

    // ================================================================
    //  批量模式（AGENTS.md §4.5 / §6.2 / §6.4）
    //  与单文件字段/命令完全隔离：独立 CTS、独立 IsBatchBusy，状态互不污染。
    //  默认 IsBatchMode=false（启动即单文件，保留原体验）；header 分段切换开批量。
    // ================================================================

    /// <summary>是否处于批量模式（header 分段切换绑定）。</summary>
    [ObservableProperty]
    private bool _isBatchMode;

    /// <summary>
    /// 模式分段切换是否可用：任一模式在跑都禁用切换（AGENTS.md §6.2）。
    /// 单文件转录中切到批量会孤立正在跑的任务，反之亦然——两套 CTS 隔离要求切换只能在双方都空闲时发生。
    /// </summary>
    public bool CanSwitchMode => !IsBusy && !IsBatchBusy;

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(CanSwitchMode));

    partial void OnIsBatchBusyChanged(bool value)
        => OnPropertyChanged(nameof(CanSwitchMode));

    /// <summary>批量转录是否进行中。影响批量 CTA / 取消 / 清空 的可用态。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelBatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearBatchListCommand))]
    private bool _isBatchBusy;

    /// <summary>批量输入文件列表（行 VM）。</summary>
    public ObservableCollection<BatchItemViewModel> BatchItems { get; } = new();

    /// <summary>批量进度整体标签（如「批量转录中…（文件 3/10：v3.mp4）」）。</summary>
    [ObservableProperty]
    private string _batchProgressLabel = string.Empty;

    /// <summary>批量进度次行（如「识别中…（段 5/12）」）。</summary>
    [ObservableProperty]
    private string _batchProgressDetail = string.Empty;

    /// <summary>批量整体进度 0~1（按文件数加权 + 当前文件段进度）。</summary>
    [ObservableProperty]
    private double _batchProgress;

    /// <summary>批量底部状态文案（空闲态提示 / 完成态汇总 / 失败计数）。</summary>
    [ObservableProperty]
    private string _batchStatusMessage = string.Empty;

    /// <summary>导出 .txt（默认勾选）。</summary>
    [ObservableProperty]
    private bool _batchOutputTxt = true;

    /// <summary>导出 .srt（默认勾选）。</summary>
    [ObservableProperty]
    private bool _batchOutputSrt = true;

    partial void OnBatchOutputTxtChanged(bool value)
    {
        StartBatchCommand.NotifyCanExecuteChanged();
        UpdateBatchIdleHint();
    }

    partial void OnBatchOutputSrtChanged(bool value)
    {
        StartBatchCommand.NotifyCanExecuteChanged();
        UpdateBatchIdleHint();
    }

    partial void OnIsBatchModeChanged(bool value)
    {
        StartBatchCommand.NotifyCanExecuteChanged();
        if (value)
        {
            // 进入批量模式时刷新批量态提示（不依赖单文件 IsBusy）。
            UpdateBatchIdleHint();
        }
    }

    /// <summary>批量当前选中的格式 flags（由两个 checkbox 派生）。</summary>
    private BatchOutputFormat SelectedBatchFormats =>
        (BatchOutputTxt ? BatchOutputFormat.Txt : BatchOutputFormat.None)
        | (BatchOutputSrt ? BatchOutputFormat.Srt : BatchOutputFormat.None);

    /// <summary>可开始批量：非忙碌、有文件、ffmpeg 可用、至少勾一个格式。</summary>
    private bool CanStartBatch()
        => !IsBatchBusy
           && BatchItems.Count > 0
           && IsFfmpegAvailable
           && SelectedBatchFormats != BatchOutputFormat.None;

    /// <summary>列表头计数文案（如「12 个文件」）。BatchItems 增删时经 CollectionChanged 刷新。</summary>
    public string BatchItemsCountText => $"{BatchItems.Count} 个文件";

    /// <summary>是否存在已完成项（驱动「清空已完成」按钮可见性 + 可用态）。</summary>
    public bool HasCompletedItems => BatchItems.Any(i => i.Status == BatchItemStatus.Done);

    /// <summary>列表是否有项（驱动列表头「移除全部」按钮可见性；与 HasCompletedItems 对称）。</summary>
    public bool HasBatchItems => BatchItems.Count > 0;

    /// <summary>可「清空已完成」：非忙碌且存在已完成项。</summary>
    private bool CanClearCompletedBatchItems() => !IsBatchBusy && HasCompletedItems;

    /// <summary>列表增删联动：刷新派生属性 + 相关命令可用态。</summary>
    private void OnBatchItemsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(BatchItemsCountText));
        OnPropertyChanged(nameof(HasCompletedItems));
        OnPropertyChanged(nameof(HasBatchItems));
        StartBatchCommand.NotifyCanExecuteChanged();
        ClearBatchListCommand.NotifyCanExecuteChanged();
        ClearCompletedBatchItemsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>构造批量行 VM 并注入三个操作回调（移除 / 打开产出 / 重试）。</summary>
    private BatchItemViewModel CreateBatchItem(string path)
    {
        var item = new BatchItemViewModel(path)
        {
            RemoveRequested = RemoveBatchItem,
            OpenOutputRequested = OpenBatchOutput,
            RetryRequested = RetryBatchItem,
        };
        return item;
    }

    /// <summary>移除单个批量行（运行中项防御性拒绝，AGENTS.md §6.6 右侧 × 按钮）。</summary>
    private void RemoveBatchItem(BatchItemViewModel item)
    {
        if (IsBatchBusy && item.Status == BatchItemStatus.Running)
        {
            return; // 运行中那行不可移除
        }
        BatchItems.Remove(item);
    }

    /// <summary>
    /// 用系统默认程序打开该文件的产出：产出 1 个 → 打开文件；多个 → 打开产出所在目录（更稳，避免多文件歧义）。
    /// 失败静默（Launcher 平台差异 / 无关联程序），仅记日志。
    /// </summary>
    private async void OpenBatchOutput(BatchItemViewModel item)
    {
        var outputs = item.OutputPaths;
        if (outputs.Count == 0)
        {
            return;
        }

        string target = outputs.Count == 1
            ? outputs[0]
            : (Path.GetDirectoryName(outputs[0]) ?? item.Directory);

        bool ok = await _fileLauncher.OpenAsync(target);
        if (!ok)
        {
            _logger.LogWarning("打开产出失败：{Target}", target);
        }
    }

    /// <summary>
    /// 重试单个失败文件：把该行重置为等待；若当前空闲则立即发起一次仅含该项的批量；
    /// 若批量在跑则仅重置状态（下一轮需用户再次「开始」）——简化语义，不实现队列插入（§6.6）。
    /// </summary>
    private void RetryBatchItem(BatchItemViewModel item)
    {
        if (item.Status != BatchItemStatus.Failed)
        {
            return;
        }

        item.MarkPending();

        if (IsBatchBusy)
        {
            BatchStatusMessage = "批量进行中，结束后点「开始批量转录」即可重跑该项。";
            return;
        }

        // 空闲：直接发起一次仅含该失败项的小批量（runner 顺序执行单项）。
        _ = StartBatchAsync(forcedInputs: [item.InputPath]);
    }

    /// <summary>清空所有已完成项（运行中禁用，AGENTS.md §6.6 列表头「清空已完成」）。</summary>
    [RelayCommand(CanExecute = nameof(CanClearCompletedBatchItems))]
    private void ClearCompletedBatchItems()
    {
        if (IsBatchBusy)
        {
            return;
        }
        for (int i = BatchItems.Count - 1; i >= 0; i--)
        {
            if (BatchItems[i].Status == BatchItemStatus.Done)
            {
                BatchItems.RemoveAt(i);
            }
        }
    }


    /// <summary>可取消批量：忙碌中。</summary>
    private bool CanCancelBatch() => IsBatchBusy;

    /// <summary>可清空列表：非忙碌且有项。</summary>
    private bool CanClearBatchList() => !IsBatchBusy && BatchItems.Count > 0;

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
        // 刷新各按钮可用态：选了文件 → 转录可用；清了结果 → 保存按钮按 CanSave 重算。
        TranscribeCommand.NotifyCanExecuteChanged();
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

    /// <summary>可转录：非忙碌、已选输入文件、且 ffmpeg 可用。三个前置条件任一不满足 CTA 灰显（§6.6）。</summary>
    private bool CanTranscribe() => !IsBusy && !string.IsNullOrEmpty(_inputPath) && IsFfmpegAvailable;

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

    // ================================================================
    //  批量模式：命令 + 编排（AGENTS.md §4.5 / §6.2 / §6.3 / §6.4）
    // ================================================================

    /// <summary>选择多个文件加入批量列表（AGENTS.md §6.2 批量分支）。</summary>
    [RelayCommand]
    private async Task PickBatchFilesAsync()
    {
        var paths = await _filePicker.PickOpenFilesAsync(
            "选择视频或音频文件（可多选）",
            "*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm", "*.mp3", "*.wav", "*.m4a", "*.flac", "*.aac");

        AddBatchInputs(paths);
    }

    /// <summary>选择一个文件夹，展开其中的媒体文件加入批量列表。</summary>
    [RelayCommand]
    private async Task PickBatchFolderAsync()
    {
        var folder = await _filePicker.PickFolderAsync("选择包含视频/音频的文件夹");
        if (folder is null)
        {
            return;
        }
        AddBatchInputs([folder]);
    }

    /// <summary>清空批量列表（运行中禁用）。</summary>
    [RelayCommand(CanExecute = nameof(CanClearBatchList))]
    private void ClearBatchList()
    {
        if (IsBatchBusy)
        {
            return;
        }
        BatchItems.Clear();
        BatchStatusMessage = string.Empty;
        StartBatchCommand.NotifyCanExecuteChanged();
    }

    /// <summary>批量拖放入口（view 层枚举所有 dropped 项后调用）。文件 + 文件夹混合均可。</summary>
    public void AcceptDroppedBatchInputs(IEnumerable<string> paths)
    {
        if (IsBatchBusy)
        {
            return;
        }
        AddBatchInputs(paths);
    }

    /// <summary>把原始路径（文件/文件夹/不存在）展开 + 去重后追加进批量列表。重复路径不重复添加。</summary>
    private void AddBatchInputs(IEnumerable<string> rawPaths)
    {
        if (IsBatchBusy)
        {
            return;
        }

        var collected = BatchInputCollector.Collect(rawPaths);
        if (collected.Count == 0)
        {
            BatchStatusMessage = "未发现可识别的媒体文件。";
            return;
        }

        var existing = BatchItems.Select(i => i.InputPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (var path in collected)
        {
            if (existing.Add(path))
            {
                BatchItems.Add(CreateBatchItem(path));
                added++;
            }
        }

        if (added > 0)
        {
            // 记最近一次输入目录（取新加入的第一项），便于下次打开同一目录。
            _settings.LastInputDirectory = Path.GetDirectoryName(collected[0]);
            try { _settingsStore.Save(_settings); } catch { /* 非关键 */ }
        }

        UpdateBatchIdleHint();
        StartBatchCommand.NotifyCanExecuteChanged();
        ClearBatchListCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 开始批量转录（顺序执行，失败跳过继续，AGENTS.md §4.5）。
    /// </summary>
    /// <param name="forcedInputs">
    /// 可选：仅重跑指定输入路径（用于失败重试）；为 null 时跑整个 BatchItems 列表。
    /// 命令入口（「开始批量转录」按钮）不传，走 null 分支。
    /// </param>
    [RelayCommand(CanExecute = nameof(CanStartBatch))]
    private async Task StartBatchAsync(IReadOnlyList<string>? forcedInputs = null)
    {
        var formats = SelectedBatchFormats;
        if (IsBatchBusy || BatchItems.Count == 0 || formats == BatchOutputFormat.None)
        {
            return;
        }

        // forcedInputs 用于失败重试：仅重跑指定路径，其余行状态不动。
        var inputPaths = forcedInputs ?? BatchItems.Select(i => i.InputPath).ToList();
        int total = inputPaths.Count;
        if (total == 0)
        {
            return;
        }

        // 仅重置本次将跑的行（forcedInputs 路径对应的行），保留其余行原状态。
        var pendingPaths = inputPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in BatchItems)
        {
            if (pendingPaths.Contains(item.InputPath))
            {
                item.MarkPending();
            }
        }

        IsBatchBusy = true;
        BatchProgress = 0;
        BatchProgressLabel = $"批量转录中…（文件 1 / 总 {total}）";
        BatchProgressDetail = string.Empty;
        BatchStatusMessage = string.Empty;
        _batchCts = new CancellationTokenSource();

        var progress = new Progress<BatchTranscriptionProgress>(OnBatchProgress);

        try
        {
            var result = await Task.Run(
                () => _batchRunner.RunAsync(inputPaths, formats, progress, _batchCts.Token),
                _batchCts.Token);

            BatchProgressLabel = "批量转录完成。";
            BatchStatusMessage = result.FailureCount == 0
                ? $"全部 {result.SuccessCount} 个文件转录完成。"
                : $"完成：{result.SuccessCount} 成功 / {result.FailureCount} 失败。";
        }
        catch (OperationCanceledException)
        {
            BatchProgressLabel = "已取消。";
            BatchStatusMessage = "批量转录已取消。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量转录发生未预期错误");
            BatchProgressLabel = "批量转录出错。";
            BatchStatusMessage = $"发生错误：{ex.Message}";
        }
        finally
        {
            IsBatchBusy = false;
            _batchCts?.Dispose();
            _batchCts = null;
        }
    }

    /// <summary>取消当前批量转录（§6.4：批量是长任务，恢复取消能力）。</summary>
    [RelayCommand(CanExecute = nameof(CanCancelBatch))]
    private void CancelBatch()
    {
        _batchCts?.Cancel();
    }

    /// <summary>批量进度回调（在 UI 线程，Progress&lt;T&gt; 已 marshal）。</summary>
    private void OnBatchProgress(BatchTranscriptionProgress p)
    {
        int total = p.TotalFiles;
        int idx = p.CurrentFileIndex; // 1-based

        // 顶行：当前文件上下文
        BatchProgressLabel = p.CurrentFileName is null
            ? $"批量转录中…（{idx} / {total}）"
            : $"批量转录中…（文件 {idx} / {total}：{p.CurrentFileName}）";

        // 次行：当前文件内部阶段（用 TranscriptionProgress 的中文标签口径）
        BatchProgressDetail = p.Stage switch
        {
            TranscriptionStage.DecodingAudio => "解码音频…",
            TranscriptionStage.VoiceActivityDetection => "语音活动检测…",
            TranscriptionStage.Recognizing when p.TotalSegments > 0 =>
                $"识别中…（段 {p.CurrentSegment} / {p.TotalSegments}）",
            TranscriptionStage.Recognizing => "识别中…",
            TranscriptionStage.Formatting => "格式化输出…",
            _ => BatchProgressDetail,
        };

        // 整体进度 = (已完成文件数 + 当前文件段进度) / 总文件数
        double fileWeight = p.TotalSegments > 0 && p.Stage == TranscriptionStage.Recognizing
            ? (double)p.CurrentSegment / p.TotalSegments
            : (p.Stage == TranscriptionStage.Done || p.Stage == TranscriptionStage.Failed ? 1 : 0);
        BatchProgress = total > 0
            ? ((idx - 1) + fileWeight) / total
            : 0;

        // 当前行：进入/刷新进行中态（在 JustCompleted 之前更新）。
        // 仅在首次进入运行态时调 MarkRunning（它会重置进度）；之后用 UpdateProgress 持续推进，
        // 避免 MarkRunning 每次把行内进度条拍回 0。
        // 按 CurrentFileName 解析行（而非索引）：失败重试等子集运行场景下索引不再对齐 BatchItems 顺序。
        if (p.Stage != TranscriptionStage.Done && p.Stage != TranscriptionStage.Failed
            && p.CurrentFileName is { } fn)
        {
            var row = BatchItems.FirstOrDefault(i => i.FileName == fn);
            if (row is not null)
            {
                if (row.Status != BatchItemStatus.Running)
                {
                    row.MarkRunning(BatchProgressDetail);
                }
                else
                {
                    row.StatusText = BatchProgressDetail;
                }
                row.UpdateProgress(fileWeight);
            }
        }

        // 文件完成事件：按 InputPath 精确解析行（比 FileName 更稳：无重名歧义）
        if (p.JustCompleted is { } outcome)
        {
            var row = BatchItems.FirstOrDefault(i =>
                string.Equals(i.InputPath, outcome.InputPath, StringComparison.OrdinalIgnoreCase));
            if (row is not null)
            {
                if (outcome.Success)
                {
                    row.MarkDone(outcome.OutputPaths);
                }
                else
                {
                    row.MarkFailed(outcome.Error ?? "未知错误");
                }
            }
        }
    }

    /// <summary>按批量状态刷新空闲态提示文案。</summary>
    private void UpdateBatchIdleHint()
    {
        if (IsBatchBusy)
        {
            return;
        }

        if (!IsFfmpegAvailable)
        {
            BatchStatusMessage = FfmpegMissingHint;
        }
        else if (BatchItems.Count == 0)
        {
            BatchStatusMessage = "选择文件或文件夹，或直接拖入。";
        }
        else if (SelectedBatchFormats == BatchOutputFormat.None)
        {
            BatchStatusMessage = "至少选择一种导出格式。";
        }
        else if (BatchStatusMessage == FfmpegMissingHint
                 || BatchStatusMessage == "选择文件或文件夹，或直接拖入。"
                 || BatchStatusMessage == "至少选择一种导出格式。")
        {
            BatchStatusMessage = string.Empty;
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
