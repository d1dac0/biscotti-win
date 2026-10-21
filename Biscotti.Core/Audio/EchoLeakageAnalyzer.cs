namespace Biscotti.Core.Audio;

internal readonly record struct EchoLeakageEstimate(int DelaySamples, double Gain, double Correlation)
{
    public double GainDecibels => Gain == 0 ? double.NegativeInfinity : 20 * Math.Log10(Math.Abs(Gain));
}

/// <summary>Measures the linear component of a playback reference found in a microphone recording.</summary>
internal static class EchoLeakageAnalyzer
{
    public static EchoLeakageEstimate Measure(ReadOnlySpan<float> reference, ReadOnlySpan<float> microphone,
        int maxDelaySamples)
    {
        if (reference.IsEmpty) throw new ArgumentException("Reference samples are required.", nameof(reference));
        if (microphone.IsEmpty) throw new ArgumentException("Microphone samples are required.", nameof(microphone));
        if (maxDelaySamples < 0) throw new ArgumentOutOfRangeException(nameof(maxDelaySamples));

        var maxDelay = Math.Min(maxDelaySamples, Math.Max(0, microphone.Length - 1));
        var bestDelay = 0;
        var bestGain = 0d;
        var bestCorrelation = 0d;
        for (var delay = 0; delay <= maxDelay; delay++)
        {
            var count = Math.Min(reference.Length, microphone.Length - delay);
            if (count == 0) continue;
            double cross = 0, referencePower = 0, microphonePower = 0;
            for (var i = 0; i < count; i++)
            {
                var source = reference[i];
                var captured = microphone[i + delay];
                cross += source * captured;
                referencePower += source * source;
                microphonePower += captured * captured;
            }
            if (referencePower <= double.Epsilon || microphonePower <= double.Epsilon) continue;
            var correlation = Math.Abs(cross) / Math.Sqrt(referencePower * microphonePower);
            if (correlation <= bestCorrelation) continue;
            bestDelay = delay;
            bestGain = cross / referencePower;
            bestCorrelation = correlation;
        }
        return new(bestDelay, bestGain, bestCorrelation);
    }
}
