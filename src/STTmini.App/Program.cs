using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace STTmini.App;

/// <summary>
/// 应用程序入口（Avalonia 12 经典桌面生命周期）。
/// </summary>
internal static class Program
{
    // 不要在任何 Avalonia / SynchronizationContext API 准备好之前调用它们。
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia 配置；可视化设计器也会用到。
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
