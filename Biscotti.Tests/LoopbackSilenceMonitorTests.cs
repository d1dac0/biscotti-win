using Biscotti.Core.Audio;

namespace Biscotti.Tests;

public sealed class LoopbackSilenceMonitorTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoMeaningfulOutputNeverArmsAutoStop()
    {
        var monitor = new LoopbackSilenceMonitor(TimeSpan.FromMinutes(5));

        monitor.Observe(FloatSamples(0.001f), 4, 1, Start);

        Assert.False(monitor.IsArmed);
        Assert.False(monitor.HasTimedOut(Start.AddHours(1)));
    }

    [Fact]
    public void MeaningfulOutputArmsAndTimesOutAfterConfiguredSilence()
    {
        var monitor = new LoopbackSilenceMonitor(TimeSpan.FromMinutes(5));
        monitor.Observe(FloatSamples(0.02f), 4, 1, Start);

        Assert.True(monitor.IsArmed);
        Assert.False(monitor.HasTimedOut(Start.AddMinutes(4).AddSeconds(59)));
        Assert.True(monitor.HasTimedOut(Start.AddMinutes(5)));
    }

    [Fact]
    public void LaterOutputResetsTheSilenceInterval()
    {
        var monitor = new LoopbackSilenceMonitor(TimeSpan.FromMinutes(5));
        monitor.Observe(FloatSamples(0.02f), 4, 1, Start);
        monitor.Observe(FloatSamples(0.001f), 4, 1, Start.AddMinutes(4));
        monitor.Observe(FloatSamples(0.03f), 4, 1, Start.AddMinutes(4).AddSeconds(30));

        Assert.False(monitor.HasTimedOut(Start.AddMinutes(9).AddSeconds(29)));
        Assert.True(monitor.HasTimedOut(Start.AddMinutes(9).AddSeconds(30)));
    }

    [Fact]
    public void ResetDisarmsAutoStopForTheNextRecording()
    {
        var monitor = new LoopbackSilenceMonitor(TimeSpan.FromMinutes(5));
        monitor.Observe(FloatSamples(0.02f), 4, 1, Start);

        monitor.Reset();

        Assert.False(monitor.IsArmed);
        Assert.False(monitor.HasTimedOut(Start.AddHours(1)));
    }

    [Fact]
    public void StereoAudioIsMeasuredAfterDownmix()
    {
        var monitor = new LoopbackSilenceMonitor(TimeSpan.FromMinutes(5));

        monitor.Observe(FloatSamples(0.04f, 0f), 8, 2, Start);

        Assert.True(monitor.IsArmed);
    }

    private static byte[] FloatSamples(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
}
