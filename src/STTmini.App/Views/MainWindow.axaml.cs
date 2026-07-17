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
        e.DragEffects = HasAnyDroppedItem(e.DataTransfer)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// 拖放落下：按当前模式分发（§4.5 / §6.2）。
    /// 单文件模式 → 取第一个文件交给 AcceptDroppedFile；
    /// 批量模式 → 枚举所有 dropped 项（文件 + 文件夹）交给 AcceptDroppedBatchInputs，由 BatchInputCollector 展开。
    /// </summary>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        var items = EnumerateDroppedItems(e.DataTransfer).ToList();
        e.DragEffects = items.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (items.Count == 0 || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (vm.IsBatchMode)
        {
            vm.AcceptDroppedBatchInputs(items);
        }
        else
        {
            vm.AcceptDroppedFile(items[0]);
        }
    }

    /// <summary>拖放数据里是否至少有一个可读本机路径的文件/文件夹项。</summary>
    private static bool HasAnyDroppedItem(IDataTransfer? data)
        => EnumerateDroppedItems(data).Any();

    /// <summary>枚举拖放数据里所有项（文件 + 文件夹）的可读本机路径，跳过 null。</summary>
    private static IEnumerable<string> EnumerateDroppedItems(IDataTransfer? data)
    {
        if (data is null)
        {
            yield break;
        }

        // TryGetFiles()（DataTransferExtensions）返回所有 dropped 项（文件 + 文件夹）；
        // 单数 TryGetFile() 只取第一个——批量模式需要全部。
        var items = data.TryGetFiles();
        if (items is null)
        {
            yield break;
        }

        foreach (var item in items)
        {
            var path = item?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                yield return path;
            }
        }
    }
}
