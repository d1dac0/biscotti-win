using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Biscotti.Core.Audio;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;
using Xunit;

namespace Biscotti.Tests;

public class AudioPipelineTests
{
    private const int InRate = 48000;
    private const int OutRate = 16000;

    private static byte[] InterleavedFloatSine(double freqHz, int frames) =>
        // stereo float32 for the WASAPI loopback path
        bytesFrom(frames, (int i) =>
        {
            var v = (float)(0.6 * Math.Sin(2 * Math.PI * freqHz * i / InRate));
            return new[] { v, v };
        });

    private static byte[] Int16MonoSine(double freqHz, int frames) =>
        // mono int16 for the mic fallback path
        Enumerable.Range(0, frames)
            .Select(i => (short)(0.6 * short.MaxValue * Math.Sin(2 * Math.PI * freqHz * i / InRate)))
            .SelectMany(BitConverter.GetBytes)
            .ToArray();

    private static byte[] bytesFrom(int frames, Func<int, float[]> frameAt)
    {
        var backing = new List<byte>();
        for (int i = 0; i < frames; i++)
            foreach (var v in frameAt(i))
                foreach (var b in BitConverter.GetBytes(v))
                    backing.Add(b);
        return backing.ToArray();
    }

    /// <summary>Goertzel power at one frequency for 16-bit samples.</summary>
    private static double GoertzelPower(short[] samples, double freq, int rate)
    {
        var w = 2 * Math.PI * freq / rate;
        var coeff = 2 * Math.Cos(w);
        double s0 = 0, s1 = 0, s2 = 0;
        foreach (var s in samples)
        {
            s0 = s + coeff * s1 - s2;
            s2 = s1; s1 = s0;
        }
        var real = s1 - s2 * Math.Cos(w);
        var imag = s2 * Math.Sin(w);
        return Math.Sqrt(real * real + imag * imag);
    }

    private static short[] ToShorts(byte[] pcm) { var arr = new short[pcm.Length / 2]; Buffer.BlockCopy(pcm, 0, arr, 0, pcm.Length); return arr; }

    [Fact]
    public void FloatPath_PassbandTone_ComesThrough()
    {
        var decimator = new AntiAliasDecimator(InRate);
        var pcm = decimator.ProcessFloat(InterleavedFloatSine(1000, 4 * InRate), 4 * InRate * 8, 2);
        var samples = ToShorts(pcm);
        Assert.NotEmpty(samples);
        var at1k = GoertzelPower(samples, 1000, OutRate);
        var rms = Math.Sqrt(samples.Average(s => (double)s * s));
        Assert.True(at1k > rms * 0.5, $"passband tone should survive: 1k={at1k} rms={rms}");
    }

    [Fact]
    public void FloatPath_TwelveKhzTone_DoesNotAliasIntoFourKhz()
    {
        // Unfiltered 3:1 decimation turns 12 kHz into 4 kHz (16-n). Feed the
        // decimator a long 12 kHz sine; the 4 kHz energy that survives must be
        // well under what a passband tone gets, expressed relative to actual
        // in-band throughput.
        var decimator = new AntiAliasDecimator(InRate);
        var aliasedPcm = decimator.ProcessFloat(InterleavedFloatSine(12000, 4 * InRate), 4 * InRate * 8, 2);
        var aliased = GoertzelPower(ToShorts(aliasedPcm), 4000, OutRate);

        // in-band reference: a 1 kHz tone through the same filter, measured AT
        // its own frequency - that is what "a tone makes it through" means here
        var reference = new AntiAliasDecimator(InRate);
        var referencePcm = reference.ProcessFloat(InterleavedFloatSine(1000, 4 * InRate), 4 * InRate * 8, 2);
        var passband = GoertzelPower(ToShorts(referencePcm), 1000, OutRate);

        // 12 kHz is attenuated ~40-plus dB by the 4-stage filter before it can
        // fold to 4 kHz; demand at least 26 dB of contrast vs. in-band content.
        Assert.True(aliased < passband * 0.05, $"aliased 4 kHz {aliased} should be far below in-band {passband}");
    }

    [Fact]
    public void Int16Path_MicFallback_ConvertsCorrectly()
    {
        var decimator = new AntiAliasDecimator(InRate);
        var pcm = decimator.ProcessInt16(Int16MonoSine(3000, InRate), InRate * 2, 1);
        var samples = ToShorts(pcm);
        Assert.NotEmpty(samples);
        // 3000 Hz passes the 7 kHz filter and lands in-band; confirm by energy
        var at3k = GoertzelPower(samples, 3000, OutRate);
        var rms = Math.Sqrt(samples.Average(s => (double)s * s));
        Assert.True(at3k > rms * 0.5, $"in-band tone should survive int16 path: 3k={at3k} rms={rms}");
    }

    [Fact]
    public void Decimator_EmitCount_MatchesRate()
    {
        var decimator = new AntiAliasDecimator(InRate);
        const int seconds = 2;
        var pcm = decimator.ProcessFloat(InterleavedFloatSine(440, seconds * InRate), seconds * InRate * 8, 2);
        var samples = ToShorts(pcm);
        var expected = seconds * OutRate;
        Assert.InRange(samples.Length, expected - 4, expected + 4);
    }
}

public class CrashSafetyTests
{
    [Fact]
    public void WavWriter_HeaderReadableWithoutClose()
    {
        // Simulated kill: never call Close(), then read the header like a
        // recorder would when it picks the file up after a crash.
        var path = Path.Combine(Path.GetTempPath(), $"wavkill-{Guid.NewGuid()}.wav");
        using (var writer = new WavStreamWriter(path))
        {
            writer.Append(new ArraySegment<byte>(new byte[48], 0, 48));
        }
        // emulate a crash: start a NEW writer on the same slot conceptually
        // by checking that header values reflect the data actually written.
        // here we only assert header coherence; Close() on dispose also fixes
        // sizes, so read before dispose would be the strict crash case - see
        // HeaderCoherent_MidStream below.
        File.Delete(path);

        var partial = new WavStreamWriter(path);
        partial.Append(new ArraySegment<byte>(new byte[96], 0, 96));
        AssertHeaderDataSize(path, 96); // Append has already rewritten it
        partial.Dispose(); // final Close in dispose
        File.Delete(path);
    }

    [Fact]
    public void HeaderCoherent_MidStream()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wavmid-{Guid.NewGuid()}.wav");
        var writer = new WavStreamWriter(path);
        writer.Append(new ArraySegment<byte>(new byte[32], 0, 32));
        // while still open (unclosed = crashed state pattern), the header must
        // already describe everything written so far
        AssertHeaderDataSize(path, 32);
        writer.Append(new ArraySegment<byte>(new byte[10], 0, 10));
        AssertHeaderDataSize(path, 42);
        writer.Dispose();
        File.Delete(path);
    }

    private static void AssertHeaderDataSize(string path, uint expected)
    {
        // The writer still holds the file open for write in the crash-sim paths,
        // so the probe must open with write sharing.
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var riff = new byte[4];
        fs.Read(riff, 0, 4);
        Assert.Equal(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }, riff);
        var header = new byte[4];
        fs.Read(header, 0, 4); // riffSize
        var riffSize = BitConverter.ToUInt32(header, 0);
        fs.Seek(40, SeekOrigin.Begin);
        fs.Read(header, 0, 4);
        var dataSize = BitConverter.ToUInt32(header, 0);
        Assert.Equal(expected, dataSize);
        Assert.Equal(36u + expected, riffSize);
    }

    [Fact]
    public void MeetingStore_Save_IsAtomicAndRoundTrips()
    {
        MeetingStore.RootOverride = Path.Combine(Path.GetTempPath(), $"biscotti-test-{Guid.NewGuid()}");
        try
        {
            var meeting = new Meeting();
            meeting.StartedUtc = DateTime.UtcNow;
            meeting.Title = "Crash safety roundtrip";
            meeting.Transcript.Add(new TranscriptLine { Speaker = "You", Text = "hello", StartSeconds = 0.5, EndSeconds = 1.3 });
            MeetingStore.Save(meeting);

            var loaded = MeetingStore.LoadAll();
            var match = loaded.FirstOrDefault(m => m.Id == meeting.Id);
            Assert.NotNull(match);
            Assert.Equal("Crash safety roundtrip", match!.Title);
            Assert.Single(match.Transcript);
            Assert.Equal("You", match.Transcript[0].Speaker);
            // no temp leftovers
            Assert.Empty(Directory.GetFiles(MeetingStore.MeetingDir(meeting.Id), "*.tmp"));
            // markdown copy generated
            Assert.True(File.Exists(Path.Combine(MeetingStore.MeetingDir(meeting.Id), "meeting.md")));
        }
        finally
        {
            var dir = MeetingStore.RootOverride!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            MeetingStore.RootOverride = null;
        }
    }

    [Fact]
    public async Task MeetingStore_LoadAllAsync_ReturnsSavedMeetings()
    {
        MeetingStore.RootOverride = Path.Combine(Path.GetTempPath(), $"biscotti-test-{Guid.NewGuid()}");
        try
        {
            var meeting = new Meeting { StartedUtc = DateTime.UtcNow, Title = "Async startup load" };
            MeetingStore.Save(meeting);

            var loaded = await MeetingStore.LoadAllAsync();

            var match = Assert.Single(loaded);
            Assert.Equal(meeting.Id, match.Id);
            Assert.Equal(meeting.Title, match.Title);
        }
        finally
        {
            var dir = MeetingStore.RootOverride!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            MeetingStore.RootOverride = null;
        }
    }
}

public class TranscriptAssemblyTests
{
    [Fact]
    public void Merge_SeparatesSpeakerStrands_OnGap()
    {
        var sys = new List<TranscriptLine>
        {
            new() { Speaker = "Speaker", Text = "a", StartSeconds = 1.0, EndSeconds = 1.5 },
            new() { Speaker = "Speaker", Text = "b", StartSeconds = 1.6, EndSeconds = 1.9 },
            new() { Speaker = "Speaker", Text = "c", StartSeconds = 4.0, EndSeconds = 4.4 },
        };
        var mic = new List<TranscriptLine> { new() { Speaker = "You", Text = "me", StartSeconds = 0.2, EndSeconds = 0.8 } };
        var merged = Biscotti.Core.Transcription.TranscriptAssembler.Merge(mic, sys);
        Assert.Equal("You", merged[0].Speaker);
        Assert.Equal(1.0, merged[1].StartSeconds);
        Assert.Equal("Speaker 1", merged[1].Speaker);
        Assert.Equal("Speaker 2", merged[3].Speaker); // c starts after the >1.5 s gap
        Assert.Equal(4, merged.Count);
    }
}
