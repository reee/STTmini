using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Skia;

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
            .UsePlatformBackend()
            // 不调 .WithInterFont()：Avalonia.Fonts.Inter（~1.9MB）仅含西文字形，
            // 而本应用 UI 为简体中文（AGENTS.md §6.1），最终走系统字体 fallback。
            // 改用系统默认 UI 字体（Windows=Segoe UI / Linux=DejaVu Sans），两平台均含中文字形。
            .LogToTrace();

    // 按 RID 显式选定平台后端（替代 Avalonia.Desktop 的 UsePlatformDetect）。
    // 不再引用聚合包 Avalonia.Desktop 后，UsePlatformDetect() 不再可用——它原本由 Desktop 包提供。
    // 改为按 csproj 定义的 STT_PLATFORM 符号直接调对应平台后端；发布 RID 已在脚本层锁定为
    // win-x64 / linux-x64（AGENTS.md §10.2），无需运行时探测。
    private static AppBuilder UsePlatformBackend(this AppBuilder b)
    {
#if WINDOWS
        // UseWin32 仅注册平台后端（窗口/输入）；渲染 Skia 与文字整形 HarfBuzz 需独立显式注册
        // （原 UsePlatformDetect 会自动配 Skia+HarfBuzz，换细粒度平台包后不再自动）。
        return b.UseWin32().UseSkia().UseHarfBuzz();
#elif LINUX
        return b.UseX11().UseSkia().UseHarfBuzz();
#else
        // 设计期兜底（无 RID 且未识别到 OS 时）：回退到运行时探测。
        // 此分支仅在 dotnet build 不带 RID 且 OS 判定失败时触发；正常发布路径不会走到。
        return b.UsePlatformDetect();
#endif
    }
}
