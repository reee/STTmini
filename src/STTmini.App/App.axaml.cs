using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using STTmini.App.Services;
using STTmini.App.ViewModels;
using STTmini.App.Views;
using STTmini.Core.Audio;
using STTmini.Core.Configuration;
using STTmini.Core.Models;
using STTmini.Core.Pipeline;
using STTmini.Core.Logging;

namespace STTmini.App;

/// <summary>
/// 应用根类型。装配 DI 容器并创建主窗口（AGENTS.md §3.1 / §6）。
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>供 XAML 设计时拿一个容器；运行期不为 null。</summary>
    public IServiceProvider Services => _services!;

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // 日志：聚合文件 logger + 调试 logger（AGENTS.md §2 / §8.4）
        Directory.CreateDirectory(AppPaths.LogDirectory);
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddProvider(new FileLoggerProvider(AppPaths.LogDirectory));
#if DEBUG
            b.AddDebug();
#endif
        });

        // 设置存储（AGENTS.md §8.2）
        services.AddSingleton<SettingsStore>();

        // 预加载设置（损坏则回退默认）；作为单例供各处读取
        services.AddSingleton(sp =>
        {
            var store = sp.GetRequiredService<SettingsStore>();
            return store.Load();
        });

        // 模型路径（AGENTS.md §8.3 / §9）
        services.AddSingleton<ModelPathResolver>(_ => new ModelPathResolver(AppPaths.ModelDirectory));

        // Core 引擎组件（AGENTS.md §4.4：每次运行新建 recognizer/VAD）
        services.AddSingleton<ITranscriptionComponentsFactory, TranscriptionComponentsFactory>();

        // 音频提取（AGENTS.md §5.4 / §11.1）。ffmpeg 路径按 Settings 在提取时解析，
        // 不在启动期解析——避免缺 ffmpeg 时无法打开 Settings 修正路径。
        services.AddSingleton<IAudioExtractor, FfmpegAudioExtractor>();

        // 转录引擎（AGENTS.md §4.1 / §7）。注册为接口：批量编排 BatchTranscriptionRunner 也依赖同一 seam。
        services.AddSingleton<ITranscriptionEngine, TranscriptionEngine>();

        // 批量编排器（AGENTS.md §4.5：顺序调用引擎 N 次，失败跳过继续）。
        services.AddSingleton<IBatchOutputWriter>(_ => FileBatchOutputWriter.Instance);
        services.AddSingleton<BatchTranscriptionRunner>();

        // 文件对话框服务（封装 Avalonia StorageProvider，便于测试）
        services.AddSingleton<IFilePickerService, FilePickerService>();

        // 系统默认程序打开文件/文件夹（封装 Avalonia LauncherExtensions，便于测试）
        services.AddSingleton<IFileLauncher, FileLauncher>();

        // ViewModels + Views
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
