using Avalonia.Platform.Storage;

namespace STTmini.App.Services;

/// <summary>
/// 文件对话框抽象（AGENTS.md §6.2）。封装 Avalonia 的 <c>StorageProvider</c>，
/// 便于在不依赖 TopLevel 的逻辑里触发选择/保存。
/// </summary>
public interface IFilePickerService
{
    /// <summary>弹出打开文件对话框，返回所选文件本地路径（取消则 null）。</summary>
    Task<string?> PickOpenFileAsync(string title, params string[] patterns);

    /// <summary>弹出保存文件对话框，返回目标文件本地路径（取消则 null）。</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, string defaultExtension);

    /// <summary>将文本写入指定路径（覆盖）。</summary>
    Task SaveTextAsync(string path, string content);
}
