using STTmini.Core.Errors;
using STTmini.Core.Models;

namespace STTmini.Core.Tests.Models;

public class ModelPathResolverTests
{
    [Fact]
    public void Paths_AreBuiltFromModelDirectory()
    {
        // 期望值用 Path.Combine 构造，使其在各平台采用本机分隔符
        // （Windows = \，Linux = /）。ModelPathResolver 内部同样用 Path.Combine，
        // 故两端必须用同一构造方式比较，避免硬编码字面量在异平台上失配。
        const string dir = "/models";
        var r = new ModelPathResolver(dir);

        Assert.Equal(Path.Combine(dir, "model.int8.onnx"), r.ParaformerModelPath);
        Assert.Equal(Path.Combine(dir, "tokens.txt"), r.ParaformerTokensPath);
        Assert.Equal(Path.Combine(dir, "am.mvn"), r.ParaformerAmvnPath);
        Assert.Equal(Path.Combine(dir, "silero_vad.onnx"), r.SileroVadPath);
    }

    [Fact]
    public void EnsureAllPresent_PassesWhenAllFilesExist()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sttmini-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in new[] { "model.int8.onnx", "tokens.txt", "am.mvn", "silero_vad.onnx" })
            {
                File.WriteAllBytes(Path.Combine(dir, f), new byte[] { 0 });
            }

            var r = new ModelPathResolver(dir);
            r.EnsureAllPresent(); // 不抛
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EnsureAllPresent_ThrowsWhenMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sttmini-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // 只放一个文件，其余缺失
            File.WriteAllBytes(Path.Combine(dir, "model.int8.onnx"), new byte[] { 0 });

            var r = new ModelPathResolver(dir);

            var ex = Assert.Throws<ModelNotFoundException>(() => r.EnsureAllPresent());
            Assert.Contains("tokens.txt", ex.MissingPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
