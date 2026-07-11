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
            Seg("第一句。", 0f),
            Seg("第二句。", 1.0f), // < 2s 阈值
        };

        var text = PlainTextFormatter.Format(segs);

        Assert.Equal("第一句。\n第二句。", text);
    }

    [Fact]
    public void LongSilence_ParagraphBreak()
    {
        var segs = new[]
        {
            Seg("第一句。", 0f),
            Seg("第二句。", 3.0f), // > 2s 阈值 → 空行
        };

        var text = PlainTextFormatter.Format(segs);

        Assert.Equal("第一句。\n\n第二句。", text);
    }

    [Fact]
    public void ExactlyThreshold_SingleNewline()
    {
        var segs = new[]
        {
            Seg("甲。", 0f),
            Seg("乙。", PlainTextFormatter.ParagraphSilenceThresholdSeconds), // 恰好 2s，不 > 2s
        };

        var text = PlainTextFormatter.Format(segs);

        Assert.Equal("甲。\n乙。", text);
    }

    [Fact]
    public void FirstSegment_NoLeadingSeparator()
    {
        var text = PlainTextFormatter.Format(new[] { Seg("开头。", 0f) });

        Assert.Equal("开头。", text);
        Assert.False(text.StartsWith('\n'));
    }

    [Fact]
    public void EmptyTextSegments_AreSkipped()
    {
        var segs = new[]
        {
            Seg("", 0f),
            Seg("实际内容。", 0.5f),
            Seg("   ", 0.5f),
        };

        var text = PlainTextFormatter.Format(segs);

        Assert.Equal("实际内容。", text);
    }

    [Fact]
    public void EmptyInput_ProducesEmpty()
    {
        Assert.Equal(string.Empty, PlainTextFormatter.Format(Array.Empty<SegmentRecognition>()));
    }
}
