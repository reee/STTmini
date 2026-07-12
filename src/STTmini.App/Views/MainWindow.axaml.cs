using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using STTmini.App.ViewModels;

namespace STTmini.App.Views;

/// <summary>
/// 主窗口（AGENTS.md §6.2）。DataContext 由 DI 注入的 MainWindowViewModel 提供。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>DI 构造注入。</summary>
    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>打开设置窗口。关闭后刷新 ffmpeg 检测——用户可能刚改了 ffmpeg 路径（§6.6）。</summary>
    private async void OpenSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var app = (App)Application.Current!;
        var settingsVm = app.Services.GetRequiredService<SettingsViewModel>();
        var view = new SettingsView { DataContext = settingsVm };
        await view.ShowDialog(this);

        // 设置弹窗关闭后：Settings 单例已拿到最新 FfmpegPathOverride，重检并刷新 CTA + 提示。
        if (DataContext is MainWindowViewModel vm)
        {
            vm.RefreshFfmpegStatus();
        }
    }

    /// <summary>拖放悬停：仅当携带文件时允许 Copy（AGENTS.md §6.2 拖放）。</summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryGetFirstFilePath(e.DataTransfer) is not null
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>拖放落下：把第一个文件路径交给 ViewModel。</summary>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        var path = TryGetFirstFilePath(e.DataTransfer);
        e.DragEffects = path is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (DataContext is MainWindowViewModel vm)
        {
            vm.AcceptDroppedFile(path);
        }
    }

    /// <summary>从拖放数据里取第一个文件的可读本机路径，无则 null。</summary>
    private static string? TryGetFirstFilePath(IDataTransfer? data)
    {
        var item = data?.TryGetFile();
        return item?.TryGetLocalPath();
    }
}
