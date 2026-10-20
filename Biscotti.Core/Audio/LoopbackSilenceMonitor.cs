using System.Buffers.Binary;

namespace Biscotti.Core.Audio;

/// <summary>Tracks meaningful loopback sound without depending on audio hardware.</summary>
internal sealed class LoopbackSilenceMonitor(TimeSpan silenceTimeout)
{
    private readonly object _gate = new();
    private DateTimeOffset? _lastActivity;

    public bool IsArmed
    {
        get { lock (_gate) return _lastActivity is not null; }
    }

    public void Observe(byte[] interleavedFloatSamples, int bytesRecorded, int channels, DateTimeOffset observedAt)
    {
        if (!ContainsMeaningfulAudio(interleavedFloatSamples, bytesRecorded, channels)) return;
        lock (_gate) _lastActivity = observedAt;
    }

    public bool HasTimedOut(DateTimeOffset now)
    {
        lock (_gate) return _lastActivity is { } last && now - last >= silenceTimeout;
    }

    public void Reset()
    {
        lock (_gate) _lastActivity = null;
    }

    private static bool ContainsMeaningfulAudio(byte[] samples, int bytesRecorded, int channels)
    {
        const int bytesPerSample = sizeof(float);
        const double minimumRms = 0.01; // -40 dBFS
        if (channels < 1) return false;
        var validBytes = Math.Clamp(bytesRecorded, 0, samples.Length);
        var frameBytes = bytesPerSample * channels;
        var frameCount = validBytes / frameBytes;
        if (frameCount == 0) return false;

        double sumSquares = 0;
        var validFrames = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var mono = 0f;
            var finiteChannels = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = frame * frameBytes + channel * bytesPerSample;
                var bits = BinaryPrimitives.ReadInt32LittleEndian(samples.AsSpan(offset, bytesPerSample));
                var value = BitConverter.Int32BitsToSingle(bits);
                if (!float.IsFinite(value)) continue;
                mono += value;
                finiteChannels++;
            }
            if (finiteChannels == 0) continue;
            mono /= finiteChannels;
            sumSquares += mono * mono;
            validFrames++;
        }

        return validFrames > 0 && Math.Sqrt(sumSquares / validFrames) >= minimumRms;
    }
}
