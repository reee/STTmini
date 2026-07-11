using STTmini.Core.Audio;

namespace STTmini.Core.Tests.Audio;

/// <summary>
/// <see cref="VadWindowSlicer"/> 的纯逻辑测试（AGENTS.md §4.2）。
/// 该切片逻辑是 VAD 喂入 bug 修复的核心可测接缝：VAD 必须按 WindowSize 逐块喂入，
/// 否则一次性整段喂入会让 sherpa-onnx 内部 circular-buffer 溢出、丢失除尾部外的全部语音。
/// </summary>
public class VadWindowSlicerTests
{
    private const int Window = VadWindowSlicer.WindowSize; // 512

    [Fact]
    public void Slice_EmptyInput_ReturnsNothing()
    {
        Assert.Empty(VadWindowSlicer.Slice(0));
    }

    [Fact]
    public void Slice_ExactlyOneWindow_ReturnsSingleFullSlice()
    {
        var slices = VadWindowSlicer.Slice(Window).ToList();
        Assert.Single(slices);
        Assert.Equal((0, Window), slices[0]);
    }

    [Fact]
    public void Slice_LongInput_ReturnsContiguousNonOverlappingCoveringWindows()
    {
        // 回归核心：长输入必须被切成"多个"窗口。整段单次喂入（bug）等价于只有 1 个窗口。
        // 模拟一段远超单窗的真实音频长度（~672s ≈ 10.76M 样本）。
        int total = 16000 * 672;

        var slices = VadWindowSlicer.Slice(total).ToList();

        // 必须是大量窗口，而不是 1 个。
        Assert.True(slices.Count > 1, "VAD 输入必须被切成多个窗口；整段单次喂入是回归 bug。");
        Assert.Equal((total + Window - 1) / Window, slices.Count);

        // 首窗从 0 开始、满窗。
        Assert.Equal(0, slices[0].Offset);
        Assert.Equal(Window, slices[0].Length);

        // 窗口连续、不重叠：每窗起点 = 上一窗起点 + 上一窗长度。
        for (int i = 1; i < slices.Count; i++)
        {
            Assert.Equal(slices[i - 1].Offset + slices[i - 1].Length, slices[i].Offset);
        }

        // 窗口完整覆盖输入，末窗可短于 WindowSize。
        var last = slices[^1];
        Assert.Equal(total, last.Offset + last.Length);
        Assert.True(last.Length > 0 && last.Length <= Window);
    }

    [Theory]
    [InlineData(1)]      // 不足一窗
    [InlineData(511)]    // 差一个样本
    [InlineData(512)]    // 恰好一窗
    [InlineData(513)]    // 多一个样本 → 两窗
    [InlineData(1024)]   // 恰好两窗
    [InlineData(1025)]   // 两窗 + 1 样本尾窗
    public void Slice_BoundaryLengths_FullyCoverInputWithCeilCount(int total)
    {
        var slices = VadWindowSlicer.Slice(total).ToList();

        // 覆盖全部样本
        Assert.Equal(total, slices.Sum(s => s.Length));

        // 每窗长度 ∈ [1, WindowSize]
        Assert.All(slices, s => Assert.InRange(s.Length, 1, Window));

        // 期望窗口数 = ceil(total / Window)
        Assert.Equal((total + Window - 1) / Window, slices.Count);
    }
}
