using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace STTmini.App.Views;

/// <summary>
/// 设置窗口（模态）。AGENTS.md §6.5。
/// </summary>
public partial class SettingsView : Window
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>点击保存或关闭时关窗。</summary>
    private void CloseOnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
