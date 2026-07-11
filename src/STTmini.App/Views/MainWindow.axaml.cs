using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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

    /// <summary>打开设置窗口。</summary>
    private void OpenSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var app = (App)Application.Current!;
        var settingsVm = app.Services.GetRequiredService<SettingsViewModel>();
        var view = new SettingsView { DataContext = settingsVm };
        view.ShowDialog(this);
    }
}
