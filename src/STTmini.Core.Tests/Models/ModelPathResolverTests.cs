using STTmini.Core.Errors;
using STTmini.Core.Models;

namespace STTmini.Core.Tests.Models;

public class ModelPathResolverTests
{
    [Fact]
    public void Paths_AreBuiltFromModelDirectory()
    {
        var r = new ModelPathResolver("/models");

        Assert.Equal("/models/model.int8.onnx", r.ParaformerModelPath);
        Assert.Equal("/models/tokens.txt", r.ParaformerTokensPath);
        Assert.Equal("/models/am.mvn", r.ParaformerAmvnPath);
        Assert.Equal("/models/silero_vad.onnx", r.SileroVadPath);
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
