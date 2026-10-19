using System.Buffers.Binary;

namespace Biscotti.Tests;

/// <summary>Deterministic PCM generators; no device or file access involved.</summary>
internal static class TestSignals
{
    public const int InRate = 48000;

    /// <summary>Interleaved stereo float32, same shape WASAPI loopback delivers.</summary>
    public static byte[] FloatStereo(double freqHz, int frames, double leftGain, double rightGain)
    {
        var bytes = new byte[frames * 2 * 4];
        for (int i = 0; i < frames; i++)
        {
            var v = Math.Sin(2 * Math.PI * freqHz * i / InRate);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 8), (float)(v * leftGain));
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 8 + 4), (float)(v * rightGain));
        }
        return bytes;
    }

    /// <summary>Mono 16-bit PCM, same shape the 48 kHz mic fallback delivers.</summary>
    public static byte[] Int16Mono(double freqHz, int frames, double gain = 0.6)
    {
        var bytes = new byte[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            var v = (short)(gain * short.MaxValue * Math.Sin(2 * Math.PI * freqHz * i / InRate));
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), v);
        }
        return bytes;
    }

    /// <summary>Feeds a signal in fixed-size packets so filter/rate state is exercised across boundaries.</summary>
    public static byte[] ProcessInChunks(Func<byte[], int, byte[]> process, byte[] packets, int bytesPerPacket)
    {
        using var output = new MemoryStream();
        for (int offset = 0; offset < packets.Length; offset += bytesPerPacket)
        {
            int count = Math.Min(bytesPerPacket, packets.Length - offset);
            var chunk = packets.AsSpan(offset, count).ToArray();
            var converted = process(chunk, count);
            output.Write(converted);
        }
        return output.ToArray();
    }

    public static short[] ToShorts(byte[] pcm)
    {
        var samples = new short[pcm.Length / 2];
        Buffer.BlockCopy(pcm, 0, samples, 0, pcm.Length);
        return samples;
    }

    public static double Rms(short[] samples) =>
        Math.Sqrt(samples.Sum(s => (double)s * s) / samples.Length);

    /// <summary>Goertzel magnitude at one frequency; good enough to compare tones.</summary>
    public static double TonePower(short[] samples, double freqHz, int sampleRate)
    {
        var w = 2 * Math.PI * freqHz / sampleRate;
        var coeff = 2 * Math.Cos(w);
        double s1 = 0, s2 = 0;
        foreach (var sample in samples)
        {
            var s0 = sample + coeff * s1 - s2;
            s2 = s1;
            s1 = s0;
        }
        var real = s1 - s2 * Math.Cos(w);
        var imag = s2 * Math.Sin(w);
        return Math.Sqrt(real * real + imag * imag);
    }
}
