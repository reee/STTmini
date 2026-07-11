using STTmini.Core.Audio;

namespace STTmini.Core.Tests.Audio;

public class FfmpegCommandBuilderTests
{
    [Fact]
    public void BuildArguments_ProducesExpectedOrder()
    {
        var args = FfmpegCommandBuilder.BuildArguments("/in.mp4", "/out.wav");

        // 应包含覆盖标志、输入、忽略视频、采样率、单声道、WAV 格式、输出
        Assert.Equal("-y", args[0]);
        Assert.Contains("-i", args);
        Assert.Contains("/in.mp4", args);
        Assert.Contains("-vn", args);
        Assert.Contains("-ar", args);
        Assert.Contains(FfmpegCommandBuilder.SampleRate.ToString(), args);
        Assert.Contains("-ac", args);
        Assert.Contains("1", args);
        Assert.Contains("-f", args);
        Assert.Contains("wav", args);
        Assert.Equal("/out.wav", args[^1]);
    }

    [Fact]
    public void SampleRate_Is16k()
    {
        Assert.Equal(16000, FfmpegCommandBuilder.SampleRate);
    }

    [Fact]
    public void BuildArguments_AlwaysOverwritesWithYFlag()
    {
        var args = FfmpegCommandBuilder.BuildArguments("a", "b");
        Assert.Equal("-y", args[0]);
    }
}
