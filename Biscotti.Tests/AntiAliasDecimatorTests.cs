using Biscotti.Core.Audio;
using Xunit;

namespace Biscotti.Tests;

/// <summary>
/// Covers the loopback anti-alias downmix (issue #2) and the int16 mic
/// fallback path (issue #13). All signals are synthetic, so these tests do not
/// need a microphone or a sound card.
/// </summary>
public class AntiAliasDecimatorTests
{
    private const int OutRate = 16000;

    [Fact]
    public void FloatPath_Stereo_RightChannelIsNotDropped()
    {
        var decimator = new AntiAliasDecimator(TestSignals.InRate);
        var rightOnly = TestSignals.FloatStereo(1000, TestSignals.InRate, leftGain: 0, rightGain: 0.6);

        var output = TestSignals.ToShorts(decimator.ProcessFloat(rightOnly, rightOnly.Length, channels: 2));

        Assert.NotEmpty(output);
        Assert.True(TestSignals.Rms(output) > 3000,
            "a tone present only in the right channel must survive the downmix");
    }

    [Fact]
    public void FloatPath_Stereo_ChannelsAreAveraged()
    {
        var leftOnlyOutput = TestSignals.ToShorts(
            new AntiAliasDecimator(TestSignals.InRate).ProcessFloat(
                TestSignals.FloatStereo(1000, TestSignals.InRate, leftGain: 0.6, rightGain: 0),
                TestSignals.InRate * 8, channels: 2));

        var bothOutput = TestSignals.ToShorts(
            new AntiAliasDecimator(TestSignals.InRate).ProcessFloat(
                TestSignals.FloatStereo(1000, TestSignals.InRate, leftGain: 0.6, rightGain: 0.6),
                TestSignals.InRate * 8, channels: 2));

        var ratio = TestSignals.Rms(bothOutput) / TestSignals.Rms(leftOnlyOutput);
        Assert.InRange(ratio, 1.9, 2.1);
    }

    [Fact]
    public void FloatPath_AttenuatesContentAboveNyquist_InsteadOfAliasingIt()
    {
        const double aliasHz = 4000; // 12 kHz folded by the 3:1 decimation

        var aliasOutput = TestSignals.ToShorts(
            new AntiAliasDecimator(TestSignals.InRate).ProcessFloat(
                TestSignals.FloatStereo(12000, TestSignals.InRate, 0.6, 0.6),
                TestSignals.InRate * 8, channels: 2));

        var passbandOutput = TestSignals.ToShorts(
            new AntiAliasDecimator(TestSignals.InRate).ProcessFloat(
                TestSignals.FloatStereo(1000, TestSignals.InRate, 0.6, 0.6),
                TestSignals.InRate * 8, channels: 2));

        var alias = TestSignals.TonePower(aliasOutput, aliasHz, OutRate);
        var passband = TestSignals.TonePower(passbandOutput, 1000, OutRate);

        Assert.True(alias < passband * 0.05,
            $"12 kHz must not fold into 4 kHz: alias={alias}, passband={passband}");
    }

    [Fact]
    public void Int16Path_PassbandTone_Survives()
    {
        var decimator = new AntiAliasDecimator(TestSignals.InRate);
        var input = TestSignals.Int16Mono(3000, TestSignals.InRate);

        var output = TestSignals.ToShorts(decimator.ProcessInt16(input, input.Length));

        var at3k = TestSignals.TonePower(output, 3000, OutRate);
        var rms = TestSignals.Rms(output);
        Assert.True(at3k > rms * 0.5, $"in-band tone lost: 3k={at3k}, rms={rms}");
    }

    [Fact]
    public void Int16Path_48kInput_ProducesExactly16kSamplesPerSecond_AcrossPackets()
    {
        // 50 ms packets, the same cadence WaveInEvent is configured with.
        var input = TestSignals.Int16Mono(440, TestSignals.InRate * 2);
        var decimator = new AntiAliasDecimator(TestSignals.InRate);

        var output = TestSignals.ProcessInChunks(
            (packet, count) => decimator.ProcessInt16(packet, count),
            input,
            bytesPerPacket: 4800);

        var samples = TestSignals.ToShorts(output);
        Assert.InRange(samples.Length, 2 * OutRate - 4, 2 * OutRate + 4);
    }
}
