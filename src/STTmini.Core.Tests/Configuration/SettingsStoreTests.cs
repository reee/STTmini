using Microsoft.Extensions.Logging.Abstractions;
using STTmini.Core.Configuration;

namespace STTmini.Core.Tests.Configuration;

public class SettingsStoreTests
{
    private static string TempFile()
        => Path.Combine(Path.GetTempPath(), "sttmini-settings-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new SettingsStore(TempFile(), NullLogger<SettingsStore>.Instance);

        var s = store.Load();

        Assert.Null(s.FfmpegPathOverride);
        Assert.Equal(OutputFormat.PlainText, s.DefaultOutputFormat);
        Assert.Null(s.LastInputDirectory);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = TempFile();
        var store = new SettingsStore(path, NullLogger<SettingsStore>.Instance);
        var original = new Settings
        {
            FfmpegPathOverride = "/usr/bin/ffmpeg",
            DefaultOutputFormat = OutputFormat.Srt,
            LastInputDirectory = "/videos",
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal("/usr/bin/ffmpeg", loaded.FfmpegPathOverride);
        Assert.Equal(OutputFormat.Srt, loaded.DefaultOutputFormat);
        Assert.Equal("/videos", loaded.LastInputDirectory);

        File.Delete(path);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        var path = TempFile();
        File.WriteAllText(path, "{ this is not valid json");

        var store = new SettingsStore(path, NullLogger<SettingsStore>.Instance);
        var s = store.Load();

        Assert.Equal(OutputFormat.PlainText, s.DefaultOutputFormat);

        File.Delete(path);
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sttmini-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "sub", "settings.json");
        try
        {
            var store = new SettingsStore(path, NullLogger<SettingsStore>.Instance);
            store.Save(new Settings { DefaultOutputFormat = OutputFormat.Srt });

            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void OutputFormat_SerializesAsString()
    {
        var path = TempFile();
        var store = new SettingsStore(path, NullLogger<SettingsStore>.Instance);
        store.Save(new Settings { DefaultOutputFormat = OutputFormat.Srt });

        var json = File.ReadAllText(path);
        Assert.Contains("\"Srt\"", json);

        File.Delete(path);
    }
}
