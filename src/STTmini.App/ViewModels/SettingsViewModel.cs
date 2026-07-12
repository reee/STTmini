using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using STTmini.Core.Audio;
using STTmini.Core.Configuration;
using STTmini.Core.Models;

namespace STTmini.App.ViewModels;

/// <summary>
/// 设置页 ViewModel（AGENTS.md §6.5）。
/// ffmpeg 路径覆盖（带状态检测）、模型目录（只读）。
/// （默认输出格式已移除——转录结果同时持有纯文本与 SRT，由主窗双保存按钮导出。）
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsStore _store;
    private readonly Settings _settings;
    private readonly ModelPathResolver _models;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(SettingsStore store, Settings settings, ModelPathResolver models, ILogger<SettingsViewModel> logger)
    {
        _store = store;
        _settings = settings;
        _models = models;
        _logger = logger;

        _ffmpegPathOverride = settings.FfmpegPathOverride ?? string.Empty;
        RefreshFfmpegStatus();
    }

    /// <summary>ffmpeg 路径覆盖（空字符串 = 用 PATH 自动检测）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _ffmpegPathOverride = string.Empty;

    /// <summary>模型目录（只读显示，AGENTS.md §6.5）。</summary>
    public string ModelDirectory => _models.ModelDirectory;

    /// <summary>ffmpeg 是否被正确找到（UI 提示）。</summary>
    [ObservableProperty]
    private string _ffmpegStatusText = string.Empty;

    /// <summary>ffmpeg 检测后的实际解析路径。</summary>
    [ObservableProperty]
    private string _ffmpegResolvedPath = string.Empty;

    /// <summary>ffmpeg 是否可用。</summary>
    public bool IsFfmpegAvailable => !string.IsNullOrEmpty(FfmpegResolvedPath);

    partial void OnFfmpegPathOverrideChanged(string value) => RefreshFfmpegStatus();

    /// <summary>立即重新检测 ffmpeg。</summary>
    [RelayCommand]
    public void RefreshFfmpegStatus()
    {
        var overrideValue = string.IsNullOrWhiteSpace(FfmpegPathOverride) ? null : FfmpegPathOverride.Trim();
        try
        {
            FfmpegResolvedPath = FfmpegLocator.Resolve(overrideValue);
            FfmpegStatusText = $"✓ 已找到：{FfmpegResolvedPath}";
        }
        catch (Exception ex)
        {
            FfmpegResolvedPath = string.Empty;
            FfmpegStatusText = $"✗ {ex.Message}";
        }

        OnPropertyChanged(nameof(IsFfmpegAvailable));
    }

    /// <summary>保存设置。</summary>
    [RelayCommand]
    public void Save()
    {
        _settings.FfmpegPathOverride = string.IsNullOrWhiteSpace(FfmpegPathOverride) ? null : FfmpegPathOverride.Trim();
        try
        {
            _store.Save(_settings);
            _logger.LogInformation("设置已保存");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存设置失败");
        }
    }
}
