using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace STTmini.Core.Configuration;

/// <summary>
/// 设置文件的加载与持久化（AGENTS.md §8.2）。
/// 文件不存在或解析失败时回退到默认设置，不抛异常（设置损坏不应阻止启动）。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly ILogger<SettingsStore> _logger;

    public SettingsStore(ILogger<SettingsStore> logger)
        : this(AppPaths.SettingsFilePath, logger) { }

    /// <summary>测试可注入路径。</summary>
    public SettingsStore(string filePath, ILogger<SettingsStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    /// <summary>加载设置；文件缺失或损坏返回默认实例。</summary>
    public Settings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new Settings();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "读取设置文件失败，回退默认设置：{Path}", _filePath);
            return new Settings();
        }
    }

    /// <summary>持久化设置。目录不存在则创建。失败抛 <see cref="IOException"/> 由调用方处理。</summary>
    public void Save(Settings settings)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
