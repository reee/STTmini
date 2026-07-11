using STTmini.Core.Recognition;
using STTmini.Core.Subtitles;

namespace STTmini.Core.Tests.Subtitles;

public class SrtFormatterTests
{
    private static SegmentRecognition Seg(float start, float end, string text, float[] timestamps)
        => new(start, end, new RecognitionResult(text, Array.Empty<string>(), timestamps), SilenceBeforeSeconds: 0f);

    [Fact]
    public void SingleSegment_SingleCue()
    {
        var seg = Seg(0f, 5f, "你好世界", new float[] { 0.5f, 0.8f, 1.1f, 1.4f });

        var srt = SrtFormatter.Format(new[] { seg });

        var expected = "1\n" +
                       "00:00:00,500 --> 00:00:01,400\n" +
                       "你好世界\n\n";
        Assert.Equal(expected, srt);
    }

    [Fact]
    public void MultipleSegments_SequentialIndices()
    {
        var segs = new[]
        {
            Seg(0f, 2f, "甲", new float[] { 0.1f }),
            Seg(3f, 5f, "乙", new float[] { 0.2f }),
        };

        var srt = SrtFormatter.Format(segs);

        Assert.Contains("1\n", srt);
        Assert.Contains("2\n", srt);
        Assert.Contains("甲", srt);
        Assert.Contains("乙", srt);
    }

    [Fact]
    public void CueBounds_UseGlobalOffset()
    {
        // 段全局起点 10s，段内 token 相对时间戳 0.5s
        var seg = Seg(10f, 15f, "测试", new float[] { 0.5f, 1.0f });

        var srt = SrtFormatter.Format(new[] { seg });

        Assert.Contains("00:00:10,500 --> 00:00:11,000", srt);
    }

    [Fact]
    public void EmptyTimestamps_FallsBackToSegmentBounds()
    {
        var seg = Seg(7f, 12f, "无时间戳", Array.Empty<float>());

        var srt = SrtFormatter.Format(new[] { seg });

        Assert.Contains("00:00:07,000 --> 00:00:12,000", srt);
    }

    [Fact]
    public void EmptyInput_ProducesEmpty()
    {
        Assert.Equal(string.Empty, SrtFormatter.Format(Array.Empty<SegmentRecognition>()));
    }
}
