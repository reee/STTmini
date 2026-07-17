using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace STTmini.App.ViewModels;

/// <summary>
/// 批量列表中单个输入文件对应的行 ViewModel（AGENTS.md §4.5 / §6.3 / §6.6）。
/// 每行展示文件名 + 状态指示（等待 / 进行中 / 成功 / 失败）+ 行内进度（运行中）+ 右侧操作区（打开产出 / 重试 / 移除）。
///
/// 「打开产出 / 移除 / 重试」三个操作需要回调父 VM（MainWindowViewModel）执行：
/// 用 <see cref="RemoveRequested"/> / <see cref="OpenOutputRequested"/> / <see cref="RetryRequested"/>
/// 三个回调字段，由父 VM 在创建行时注入。这样 item 不反向持有父 VM 引用，避免耦合
/// （也避免命令参数走 CommandParameter 字符串黑魔法）。
/// </summary>
public partial class BatchItemViewModel : ViewModelBase
{
    /// <param name="inputPath">输入文件全路径。</param>
    public BatchItemViewModel(string inputPath)
    {
        InputPath = inputPath;
        FileName = Path.GetFileName(inputPath);
        Directory = Path.GetDirectoryName(inputPath) ?? string.Empty;
        ApplyPendingState();
    }

    /// <summary>输入文件全路径。</summary>
    public string InputPath { get; }

    /// <summary>文件名（含扩展名，无路径），用于显示。</summary>
    public string FileName { get; }

    /// <summary>所在目录（不显示，仅保留供「打开产出」时定位产出目录）。</summary>
    public string Directory { get; }

    /// <summary>行状态。</summary>
    [ObservableProperty]
    private BatchItemStatus _status = BatchItemStatus.Pending;

    /// <summary>状态文案（如「等待」「识别中…（段 5/12）」「✓ 已完成」「✕ 失败」）。</summary>
    [ObservableProperty]
    private string _statusText = "等待";

    /// <summary>失败时的错误说明（成功时为空）。UI 用红色显示。</summary>
    [ObservableProperty]
    private string _error = string.Empty;

    /// <summary>该文件成功产出的输出文件名列表（用于行尾展示「→ v1.txt, v1.srt」）。</summary>
    [ObservableProperty]
    private string _outputSummary = string.Empty;

    /// <summary>该文件成功产出的输出文件全路径列表（供「打开」操作使用）。</summary>
    public IReadOnlyList<string> OutputPaths { get; private set; } = Array.Empty<string>();

    /// <summary>行内进度 0~1（仅运行中有意义，驱动行内 2px 细进度条，AGENTS.md §6.6）。</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>是否正在转录（驱动行内进度条 IsVisible）。</summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>右侧 × 移除按钮是否可用（运行中禁用）。</summary>
    [ObservableProperty]
    private bool _canRemove = true;

    /// <summary>「打开产出」按钮是否可见（成功且有产出）。</summary>
    [ObservableProperty]
    private bool _canOpenOutput;

    /// <summary>「重试」按钮是否可见（失败）。</summary>
    [ObservableProperty]
    private bool _canRetry;

    /// <summary>「请求移除本行」回调（由父 VM 注入）。</summary>
    public Action<BatchItemViewModel>? RemoveRequested { get; set; }

    /// <summary>「请求打开产出」回调（由父 VM 注入）。</summary>
    public Action<BatchItemViewModel>? OpenOutputRequested { get; set; }

    /// <summary>「请求重试」回调（由父 VM 注入）。</summary>
    public Action<BatchItemViewModel>? RetryRequested { get; set; }

    /// <summary>标记为进行中，附带状态文案；重置行内进度。</summary>
    public void MarkRunning(string statusText)
    {
        Status = BatchItemStatus.Running;
        StatusText = statusText;
        Error = string.Empty;
        Progress = 0;
        IsRunning = true;
        CanRemove = false;
        CanOpenOutput = false;
        CanRetry = false;
    }

    /// <summary>更新运行中行的行内进度（不改变其它状态字段）。</summary>
    public void UpdateProgress(double progress) => Progress = progress;

    /// <summary>标记为成功，附带产出文件路径列表（生成展示摘要）。</summary>
    public void MarkDone(IReadOnlyList<string> outputPaths)
    {
        OutputPaths = outputPaths;
        var names = outputPaths.Select(Path.GetFileName).ToList();
        OutputSummary = names.Count > 0 ? "→ " + string.Join(", ", names) : string.Empty;
        Status = BatchItemStatus.Done;
        StatusText = "✓ 已完成";
        Error = string.Empty;
        IsRunning = false;
        CanRemove = true;
        CanOpenOutput = outputPaths.Count > 0;
        CanRetry = false;
    }

    /// <summary>标记为失败，附带错误说明。</summary>
    public void MarkFailed(string error)
    {
        Status = BatchItemStatus.Failed;
        StatusText = "✕ 失败";
        Error = error;
        IsRunning = false;
        CanRemove = true;
        CanOpenOutput = false;
        CanRetry = true;
    }

    /// <summary>重置为排队等待（批量开始前 / 重试前清掉上次运行残留状态）。</summary>
    public void MarkPending()
    {
        ApplyPendingState();
        OutputPaths = Array.Empty<string>();
        OutputSummary = string.Empty;
    }

    private void ApplyPendingState()
    {
        Status = BatchItemStatus.Pending;
        StatusText = "等待";
        Error = string.Empty;
        Progress = 0;
        IsRunning = false;
        CanRemove = true;
        CanOpenOutput = false;
        CanRetry = false;
    }

    [RelayCommand]
    private void RequestRemove() => RemoveRequested?.Invoke(this);

    [RelayCommand]
    private void RequestOpenOutput() => OpenOutputRequested?.Invoke(this);

    [RelayCommand]
    private void RequestRetry() => RetryRequested?.Invoke(this);
}

/// <summary>批量列表行的四种状态（驱动 UI 图标/颜色）。</summary>
public enum BatchItemStatus
{
    /// <summary>排队等待。</summary>
    Pending,

    /// <summary>正在转录。</summary>
    Running,

    /// <summary>成功完成。</summary>
    Done,

    /// <summary>失败。</summary>
    Failed,
}
