using STTmini.Core.Recognition;
using STTmini.Core.Subtitles;

namespace STTmini.Core.Tests.Subtitles;

public class PlainTextFormatterTests
{
    private static SegmentRecognition Seg(string text, float silenceBefore)
        => new(0f, 1f, new RecognitionResult(text, Array.Empty<string>(), Array.Empty<float>()), silenceBefore);

    [Fact]
    public void ShortSilence_SingleNewlineBetweenSegments()
    {
        var segs = new[]
        {
            Seg("第一句", 0f),
            Seg("第二句", 0.3f), // < 0.6s 阈值
        };

        var text = PlainTextFormatter.Format(segs);

        Assert.Equal("第一句\n第二句", text);
    }

    [Fact]
    public void LongSilence_ParagraphBreak()
    {
        var segs = new[]
        {
            Seg("第一句", 0f),
            Seg("第二句", 1.0f), // > 0.6s 阈值 → 空行
        };

        var text = PlainTextFormatter.Format(segs);

        Assert.Equal("第一句\n\n第二句", text);
    }

    [Fact]
    public void SilenceExactlyAtThreshold_StillSingleNewline()
    {
        // 恰好等于阈值不算 > 阈值 → 单换行
        var segs = new[]
        {
            Seg("甲", 0f),
            Seg("乙", PlainTextFormatter.ParagraphSilenceThresholdSeconds),
        };

        var text = PlainTextFormatter.Format(segs);

        Assert.Equal("甲\n乙", text);
    }

    [Fact]
    public void SilenceAroundThreshold_SwitchesFromNewlineToBlankLine()
    {
        // 锁定阈值边界，防止常量被误改（实测快语速视频 gap 中位 0.47s、p75 0.67s）。
        const float t = PlainTextFormatter.ParagraphSilenceThresholdSeconds;

        var justBelow = PlainTextFormatter.Format(new[] { Seg("甲", 0f), Seg("乙", t - 0.1f) });
        Assert.Equal("甲\n乙", justBelow);

        var justAbove = PlainTextFormatter.Format(new[] { Seg("甲", 0f), Seg("乙", t + 0.1f) });
        Assert.Equal("甲\n\n乙", justAbove);
    }

    [Fact]
    public void FirstSegment_NoLeadingSeparator()
    {
        var text = PlainTextFormatter.Format(new[] { Seg("开头", 0f) });

        Assert.Equal("开头", text);
        Assert.False(text.StartsWith('\n'));
    }

    [Fact]
    public void EmptyTextSegments_AreSkipped()
    {
        var segs = new[]
        {
            Seg("", 0f),
            Seg("实际内容", 0.5f),
            Seg("   ", 0.5f),
        };

        var text = PlainTextFormatter.Format(segs);

        Assert.Equal("实际内容", text);
    }

    [Fact]
    public void EmptyInput_ProducesEmpty()
    {
        Assert.Equal(string.Empty, PlainTextFormatter.Format(Array.Empty<SegmentRecognition>()));
    }
}
