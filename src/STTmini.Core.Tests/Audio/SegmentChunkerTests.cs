using STTmini.Core.Audio;

namespace STTmini.Core.Tests.Audio;

public class SegmentChunkerTests
{
    [Fact]
    public void ShortSegment_ReturnsSingleChunkWithGlobalBounds()
    {
        // 10 秒段（< 25s 阈值），起点 100 秒
        int ten = 10 * AudioConstants.SampleRate;
        var samples = new float[ten];
        var seg = new SpeechSegment(StartSeconds: 100f, Samples: samples);

        var chunks = SegmentChunker.Chunk(seg);

        Assert.Single(chunks);
        Assert.Equal(100f, chunks[0].GlobalStartSeconds);
        Assert.Equal(110f, chunks[0].GlobalEndSeconds, precision: 1);
        Assert.Equal(ten, chunks[0].Samples.Length);
    }

    [Fact]
    public void ExactlyAtLimit_ReturnsSingleChunk()
    {
        int atLimit = (int)(SegmentChunker.MaxSegmentSeconds * AudioConstants.SampleRate);
        var samples = new float[atLimit];
        var seg = new SpeechSegment(0f, samples);

        var chunks = SegmentChunker.Chunk(seg);

        Assert.Single(chunks);
    }

    [Fact]
    public void OverLimit_SplitsAt25sWindows()
    {
        // 60 秒段 → 应切为 25s + 25s + 10s 三段
        int sixty = 60 * AudioConstants.SampleRate;
        var samples = new float[sixty];
        var seg = new SpeechSegment(StartSeconds: 50f, samples);

        var chunks = SegmentChunker.Chunk(seg);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(50f, chunks[0].GlobalStartSeconds, precision: 1);
        Assert.Equal(75f, chunks[0].GlobalEndSeconds, precision: 1);
        Assert.Equal(75f, chunks[1].GlobalStartSeconds, precision: 1);
        Assert.Equal(100f, chunks[1].GlobalEndSeconds, precision: 1);
        Assert.Equal(100f, chunks[2].GlobalStartSeconds, precision: 1);
        Assert.Equal(110f, chunks[2].GlobalEndSeconds, precision: 1);

        // 样本数守恒
        long total = chunks.Sum(c => c.Samples.LongLength);
        Assert.Equal(sixty, total);
    }

    [Fact]
    public void Chunks_AreContiguous()
    {
        int seventy = 70 * AudioConstants.SampleRate;
        var samples = new float[seventy];
        var seg = new SpeechSegment(StartSeconds: 0f, samples);

        var chunks = SegmentChunker.Chunk(seg);

        for (int i = 1; i < chunks.Count; i++)
        {
            Assert.Equal(chunks[i - 1].GlobalEndSeconds, chunks[i].GlobalStartSeconds, precision: 2);
        }
    }

    [Fact]
    public void GlobalBounds_PreserveSampleCounts()
    {
        int thirty = 30 * AudioConstants.SampleRate;
        var samples = new float[thirty];
        var seg = new SpeechSegment(StartSeconds: 200f, samples);

        var chunks = SegmentChunker.Chunk(seg);

        // 30s = 25s + 5s
        Assert.Equal(2, chunks.Count);
        Assert.Equal(25f, chunks[0].GlobalEndSeconds - chunks[0].GlobalStartSeconds, precision: 1);
        Assert.Equal(5f, chunks[1].GlobalEndSeconds - chunks[1].GlobalStartSeconds, precision: 1);
    }
}
