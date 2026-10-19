using System.Buffers.Binary;

namespace Biscotti.Core.Audio;

/// <summary>
/// Second-order Butterworth low-pass, RBJ cookbook form. Cascaded four times for
/// ~48 dB/octave rolloff.
/// </summary>
internal struct Biquad
{
    private double _b0, _b1, _b2, _a1, _a2; // normalized by a0
    private double _x1, _x2, _y1, _y2;

    public Biquad(int sampleRate, double cutoffHz)
    {
        const double q = 0.7071;
        var w0 = 2 * Math.PI * cutoffHz / sampleRate;
        var cosW0 = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * q);
        var a0 = 1 + alpha;
        _b0 = (1 - cosW0) / 2 / a0;
        _b1 = (1 - cosW0) / a0;
        _b2 = _b0;
        _a1 = -2 * cosW0 / a0;
        _a2 = (1 - alpha) / a0;
    }

    public float Process(float x)
    {
        var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1; _x1 = x;
        _y2 = _y1; _y1 = y;
        return (float)y;
    }
}

/// <summary>
/// Anti-alias downmixer: turns interleaved input (32-bit float WASAPI loopback or
/// 16-bit PCM mic) into mono 16 kHz bytes for Whisper. Filters EVERY input frame
/// through a cascaded biquad before emitting at the target rate — the raw
/// decimation this replaces aliased anything above 8 kHz into the speech band
/// (issue #2). Filter state persists across packets via Bresenham emit
/// scheduling, so drift is negligible over hour-long meetings and both channels
/// stay artifact-free.
/// </summary>
public sealed class AntiAliasDecimator
{
    // Four stages at the same cutoff: ~48 dB/octave, so content above the
    // 8 kHz output Nyquist is far down by the time it can fold back.
    private const int StageCount = 4;

    private readonly Biquad[] _stages = new Biquad[StageCount];
    private readonly int _inRate;
    private double _emitCarry;

    public AntiAliasDecimator(int inSampleRate = 48000)
    {
        if (inSampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(inSampleRate));

        _inRate = inSampleRate;
        // Cutoff below the 8 kHz output Nyquist with margin; for an input that
        // is already at or near 16 kHz, stay below its own Nyquist instead.
        var cutoff = Math.Min(7000.0, inSampleRate * 0.45);
        for (int i = 0; i < StageCount; i++)
            _stages[i] = new Biquad(inSampleRate, cutoff);
    }

    /// <summary>
    /// WASAPI loopback path: interleaved IEEE float, any channel count.
    /// Channels are averaged in the float domain before filtering, so a stereo
    /// call is not reduced to its left speaker only.
    /// </summary>
    public byte[] ProcessFloat(byte[] src, int count, int channels)
    {
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels), "At least one channel is required.");

        const int bytesPerSample = 4;
        int frameBytes = bytesPerSample * channels;
        int frames = count / frameBytes;
        if (frames == 0) return Array.Empty<byte>();

        // long math: frames * 16000 overflows int for packets above ~2 min
        var capacity = (int)Math.Min((long)frames * AudioConfig.SampleRate / _inRate + 2, frames + 2);
        var outSamples = new List<short>(capacity);
        for (int i = 0; i < frames; i++)
        {
            int offset = i * frameBytes;
            float mono = 0;
            for (int ch = 0; ch < channels; ch++)
                mono += BitConverter.ToSingle(src, offset + ch * bytesPerSample);
            mono /= channels;

            mono = Filter(mono);
            if (ShouldEmit()) outSamples.Add(ClampToShort(mono));
        }
        return ShortsToBytes(outSamples);
    }

    /// <summary>
    /// Mic fallback path: interleaved 16-bit PCM, any channel count. The
    /// fallback capture asks for mono, but averaging here keeps this correct if
    /// that ever changes.
    /// </summary>
    public byte[] ProcessInt16(byte[] src, int count, int channels = 1)
    {
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels), "At least one channel is required.");

        const int bytesPerSample = 2;
        int frameBytes = bytesPerSample * channels;
        int frames = count / frameBytes;
        if (frames == 0) return Array.Empty<byte>();

        var capacity = (int)Math.Min((long)frames * AudioConfig.SampleRate / _inRate + 2, frames + 2);
        var outSamples = new List<short>(capacity);
        for (int i = 0; i < frames; i++)
        {
            int offset = i * frameBytes;
            int acc = 0;
            for (int ch = 0; ch < channels; ch++)
                acc += BinaryPrimitives.ReadInt16LittleEndian(src.AsSpan(offset + ch * bytesPerSample, bytesPerSample));
            float mono = acc / (float)channels / 32768f;

            mono = Filter(mono);
            if (ShouldEmit()) outSamples.Add(ClampToShort(mono));
        }
        return ShortsToBytes(outSamples);
    }

    private float Filter(float sample)
    {
        for (int i = 0; i < _stages.Length; i++)
            sample = _stages[i].Process(sample);
        return sample;
    }

    /// <summary>
    /// Bresenham-style scheduler: emit one output sample per inRate/targetRate
    /// input frames, carrying the remainder so packet boundaries never drift.
    /// </summary>
    private bool ShouldEmit()
    {
        _emitCarry += AudioConfig.SampleRate;
        if (_emitCarry < _inRate) return false;
        _emitCarry -= _inRate;
        return true;
    }

    private static short ClampToShort(float v)
    {
        v = Math.Clamp(v, -1f, 1f);
        return (short)MathF.Round(v * short.MaxValue);
    }

    private static byte[] ShortsToBytes(List<short> samples)
    {
        var bytes = new byte[samples.Count * 2];
        Buffer.BlockCopy(samples.ToArray(), 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
