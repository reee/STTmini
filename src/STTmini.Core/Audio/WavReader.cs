using System.IO;
using STTmini.Core.Errors;

namespace STTmini.Core.Audio;

/// <summary>
/// 将 16kHz mono PCM WAV 读取为 float[] 样本（范围 [-1,1]）。
/// 仅支持流水线自身 ffmpeg 产出的 WAV（PCM16 / 单声道），不做通用 WAV 解析。
/// </summary>
public static class WavReader
{
    /// <summary>
    /// 读取 WAV 文件的 PCM 样本。
    /// </summary>
    /// <exception cref="AudioExtractionException">WAV 格式与预期不符。</exception>
    public static float[] ReadMonoPcm16(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        // RIFF header
        Span<byte> riff = stackalloc byte[4];
        Read(br, riff);
        if (System.Text.Encoding.ASCII.GetString(riff) != "RIFF")
        {
            throw new AudioExtractionException("WAV 格式异常（非 RIFF）", "RIFF header missing");
        }

        Read(br, stackalloc byte[4]); // chunk size
        Read(br, riff);
        if (System.Text.Encoding.ASCII.GetString(riff) != "WAVE")
        {
            throw new AudioExtractionException("WAV 格式异常（非 WAVE）", "WAVE header missing");
        }

        // fmt + data chunks（按 chunk 遍历，fmt 不一定紧随）
        short numChannels = 0;
        int sampleRate = 0;
        short bitsPerSample = 0;
        byte[]? dataBytes = null;

        while (fs.Position < fs.Length)
        {
            Read(br, riff);
            int chunkSize = br.ReadInt32(); // 小端
            var id = System.Text.Encoding.ASCII.GetString(riff);

            if (id == "fmt ")
            {
                short audioFormat = br.ReadInt16();
                numChannels = br.ReadInt16();
                sampleRate = br.ReadInt32();
                _ = br.ReadInt32(); // byte rate
                _ = br.ReadInt16(); // block align
                bitsPerSample = br.ReadInt16();
                // fmt 的剩余字节跳过
                int fmtConsumed = 16;
                if (fmtConsumed < chunkSize)
                {
                    fs.Seek(chunkSize - fmtConsumed, SeekOrigin.Current);
                }
            }
            else if (id == "data")
            {
                dataBytes = br.ReadBytes(chunkSize);
                if (dataBytes.Length < chunkSize && fs.Position < fs.Length)
                {
                    // chunk size 不足时继续读到末尾（某些工具的 chunk size 不精确）
                }
                break;
            }
            else
            {
                fs.Seek(chunkSize, SeekOrigin.Current);
            }

            // WAV chunks 按偶数字节对齐
            if ((chunkSize & 1) == 1 && fs.Position < fs.Length)
            {
                fs.Seek(1, SeekOrigin.Current);
            }
        }

        if (dataBytes is null)
        {
            throw new AudioExtractionException("WAV 无 data chunk", "data chunk missing");
        }

        if (sampleRate != AudioConstants.SampleRate)
        {
            throw new AudioExtractionException(
                $"WAV 采样率不符：{sampleRate}（预期 {AudioConstants.SampleRate}）", "sample rate mismatch");
        }

        if (numChannels != 1)
        {
            throw new AudioExtractionException(
                $"WAV 非单声道：{numChannels}", "not mono");
        }

        if (bitsPerSample != 16)
        {
            throw new AudioExtractionException(
                $"WAV 位深不符：{bitsPerSample}（预期 16）", "bits per sample mismatch");
        }

        // PCM16 小端 → float[-1,1]
        int sampleCount = dataBytes.Length / 2;
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short v = (short)(dataBytes[i * 2] | (dataBytes[i * 2 + 1] << 8));
            samples[i] = v / 32768f;
        }

        return samples;
    }

    private static void Read(BinaryReader br, Span<byte> dst)
    {
        int n = br.Read(dst);
        if (n != dst.Length)
        {
            throw new AudioExtractionException("WAV 文件意外结束", "unexpected EOF");
        }
    }
}
