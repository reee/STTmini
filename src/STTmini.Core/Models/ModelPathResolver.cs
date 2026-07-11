using STTmini.Core.Errors;

namespace STTmini.Core.Models;

/// <summary>
/// 解析模型目录下的各类模型文件路径，并校验存在性（AGENTS.md §4.2 / §11.1）。
/// 纯逻辑 + 文件系统探测，可在注入模拟目录的测试中验证。
/// </summary>
public sealed class ModelPathResolver
{
    private readonly string _modelDirectory;

    public ModelPathResolver(string modelDirectory)
    {
        _modelDirectory = modelDirectory;
    }

    public string ModelDirectory => _modelDirectory;

    public string ParaformerModelPath => Path.Combine(_modelDirectory, ModelFileNames.ParaformerModel);
    public string ParaformerTokensPath => Path.Combine(_modelDirectory, ModelFileNames.ParaformerTokens);
    public string ParaformerAmvnPath => Path.Combine(_modelDirectory, ModelFileNames.ParaformerAmvn);
    public string SileroVadPath => Path.Combine(_modelDirectory, ModelFileNames.SileroVad);

    /// <summary>
    /// 校验全部模型文件存在；任一缺失抛 <see cref="ModelNotFoundException"/>。
    /// </summary>
    public void EnsureAllPresent()
    {
        var required = new (string Label, string Path)[]
        {
            ("Paraformer 模型", ParaformerModelPath),
            ("Paraformer tokens", ParaformerTokensPath),
            ("Paraformer am.mvn", ParaformerAmvnPath),
            ("Silero VAD", SileroVadPath),
        };

        foreach (var (label, path) in required)
        {
            if (!File.Exists(path))
            {
                throw new ModelNotFoundException(
                    $"缺失模型文件（{label}）：{path}", path);
            }
        }
    }
}
