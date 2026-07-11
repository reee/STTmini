using STTmini.Core.Subtitles;

namespace STTmini.Core.Tests.Subtitles;

public class TimestampMathTests
{
    [Fact]
    public void ToGlobal_AddsOffsetToEachTimestamp()
    {
        var relative = new float[] { 0.1f, 0.5f, 1.2f };

        var global = TimestampMath.ToGlobal(relative, globalOffsetSeconds: 10f);

        Assert.Equal(new float[] { 10.1f, 10.5f, 11.2f }, global);
    }

    [Fact]
    public void ToGlobal_EmptyInput_ReturnsEmpty()
    {
        var global = TimestampMath.ToGlobal(Array.Empty<float>(), 5f);
        Assert.Empty(global);
    }

    [Fact]
    public void CueBounds_FirstAndLastToken()
    {
        var ts = new float[] { 1.0f, 2.0f, 3.5f };

        var (start, end) = TimestampMath.CueBounds(ts, segmentGlobalStart: 0f, segmentGlobalEnd: 4f);

        Assert.Equal(1.0f, start);
        Assert.Equal(3.5f, end);
    }

    [Fact]
    public void CueBounds_EmptyTimestamps_FallsBackToSegmentBounds()
    {
        var (start, end) = TimestampMath.CueBounds(Array.Empty<float>(), segmentGlobalStart: 5f, segmentGlobalEnd: 8f);

        Assert.Equal(5f, start);
        Assert.Equal(8f, end);
    }

    [Fact]
    public void CueBounds_NotCappedBySegmentEnd_AgentsMd_5_2()
    {
        // AGENTS.md §5.2：cue 边界由 token 时间戳决定，不用 VAD/子段边界。
        // 即便末 token 时间戳超出子段全局终点，cue 终点仍取 token 值，不被段终点封顶。
        var ts = new float[] { 1.0f, 9.0f };

        var (start, end) = TimestampMath.CueBounds(ts, segmentGlobalStart: 0f, segmentGlobalEnd: 5f);

        Assert.Equal(1.0f, start);
        Assert.Equal(9.0f, end);
    }

    [Fact]
    public void CueBounds_LastBeforeFirst_Swaps()
    {
        var ts = new float[] { 3.0f, 1.0f };

        var (start, end) = TimestampMath.CueBounds(ts, segmentGlobalStart: 0f, segmentGlobalEnd: 10f);

        Assert.Equal(3.0f, start);
        Assert.Equal(3.0f, end); // end 至少等于 start
    }

    [Theory]
    [InlineData(0f, "00:00:00,000")]
    [InlineData(1.5f, "00:00:01,500")]
    [InlineData(61.25f, "00:01:01,250")]
    [InlineData(3661.999f, "01:01:01,999")]
    [InlineData(-5f, "00:00:00,000")] // 负值钳到 0
    public void FormatSrtTimecode_Correct(float seconds, string expected)
    {
        Assert.Equal(expected, TimestampMath.FormatSrtTimecode(seconds));
    }

    [Fact]
    public void FormatSrtTimecode_RoundsMilliseconds()
    {
        // 0.9995s → 1000ms → 进位到 1s
        Assert.Equal("00:00:01,000", TimestampMath.FormatSrtTimecode(0.9995f));
    }
}
