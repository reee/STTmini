using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace STTmini.App.Services;

/// <summary>
/// <see cref="IFilePickerService"/> 的 Avalonia 实现（StorageProvider API）。
/// 从当前应用主窗口获取 TopLevel。
/// </summary>
public sealed class FilePickerService : IFilePickerService
{
    private static TopLevel? TopLevel =>
        (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)
        ?.MainWindow;

    /// <inheritdoc/>
    public async Task<string?> PickOpenFileAsync(string title, params string[] patterns)
    {
        var tl = TopLevel;
        if (tl is null)
        {
            return null;
        }

        var filter = patterns.Length > 0
            ? new[] { new FilePickerFileType("媒体文件") { Patterns = patterns }, FilePickerFileTypes.All }
            : new[] { FilePickerFileTypes.All };

        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filter,
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <inheritdoc/>
    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string defaultExtension)
    {
        var tl = TopLevel;
        if (tl is null)
        {
            return null;
        }

        var file = await tl.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = defaultExtension,
        });

        return file?.TryGetLocalPath();
    }

    /// <inheritdoc/>
    public async Task SaveTextAsync(string path, string content)
    {
        await File.WriteAllTextAsync(path, content);
    }
}
