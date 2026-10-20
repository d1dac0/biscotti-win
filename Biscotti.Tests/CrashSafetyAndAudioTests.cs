using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biscotti.Core.Audio;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;
using Biscotti.Mcp;
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
            meeting.TimestampedNotes.Add(new TimestampedMeetingNote { Text = "Follow up on this", StartSeconds = 0.2 });
            MeetingStore.Save(meeting);

            var loaded = MeetingStore.LoadAll();
            var match = loaded.FirstOrDefault(m => m.Id == meeting.Id);
            Assert.NotNull(match);
            Assert.Equal("Crash safety roundtrip", match!.Title);
            Assert.Single(match.Transcript);
            Assert.Equal("You", match.Transcript[0].Speaker);
            Assert.Single(match.TimestampedNotes);
            Assert.Equal("Follow up on this", match.TimestampedNotes[0].Text);
            var markdownPath = Path.Combine(MeetingStore.MeetingDir(meeting.Id), "meeting.md");
            var markdownFile = File.ReadAllBytes(markdownPath);
            Assert.True(markdownFile.AsSpan().StartsWith("BISCOTTI-DPAPI:1\n"u8));
            Assert.DoesNotContain("Follow up on this", Encoding.UTF8.GetString(markdownFile), StringComparison.Ordinal);
            var jsonFile = File.ReadAllBytes(Path.Combine(MeetingStore.MeetingDir(meeting.Id), "meeting.json"));
            Assert.True(jsonFile.AsSpan().StartsWith("BISCOTTI-DPAPI:1\n"u8));
            Assert.DoesNotContain("Crash safety roundtrip", Encoding.UTF8.GetString(jsonFile), StringComparison.Ordinal);
            var markdownBytes = ProtectedMeetingText.ReadAndMigrate(markdownPath);
            var markdown = Encoding.UTF8.GetString(markdownBytes);
            CryptographicOperations.ZeroMemory(markdownBytes);
            Assert.True(markdown.IndexOf("**Note**: Follow up on this", StringComparison.Ordinal)
                < markdown.IndexOf("**You**: hello", StringComparison.Ordinal));
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
    public void MeetingStore_MigratesLegacyJsonAndMarkdownIndependently()
    {
        MeetingStore.RootOverride = Path.Combine(Path.GetTempPath(), $"biscotti-text-migrate-{Guid.NewGuid()}");
        ProtectedMeetingText.ProtectorOverride = new PrefixMeetingTextProtector();
        try
        {
            var meeting = new Meeting { StartedUtc = DateTime.UtcNow, Title = "Legacy meeting" };
            meeting.Transcript.Add(new TranscriptLine { Speaker = "You", Text = "private words", StartSeconds = 1, EndSeconds = 2 });
            var dir = MeetingStore.MeetingDir(meeting.Id);
            Directory.CreateDirectory(dir);
            var jsonPath = Path.Combine(dir, "meeting.json");
            var markdownPath = Path.Combine(dir, "meeting.md");
            var json = JsonSerializer.SerializeToUtf8Bytes(meeting, new JsonSerializerOptions { WriteIndented = true });
            var markdown = Encoding.UTF8.GetBytes("# User-edited legacy note\n\nprivate words\n");
            File.WriteAllBytes(jsonPath, json);
            File.WriteAllBytes(markdownPath, markdown);

            var loaded = Assert.Single(MeetingStore.LoadMetadataOnly());

            Assert.Equal(meeting.Id, loaded.Id);
            Assert.True(File.ReadAllBytes(jsonPath).AsSpan().StartsWith("BISCOTTI-DPAPI:1\n"u8));
            Assert.True(File.ReadAllBytes(markdownPath).AsSpan().StartsWith("BISCOTTI-DPAPI:1\n"u8));
            Assert.Equal(markdown, ProtectedMeetingText.ReadAndMigrate(markdownPath));
            CryptographicOperations.ZeroMemory(json);
            CryptographicOperations.ZeroMemory(markdown);
        }
        finally
        {
            var dir = MeetingStore.RootOverride!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            MeetingStore.RootOverride = null;
            ProtectedMeetingText.ProtectorOverride = null;
        }
    }

    [Fact]
    public void MeetingStore_DoesNotTreatTamperedProtectedJsonAsPlaintextOrSkipIt()
    {
        MeetingStore.RootOverride = Path.Combine(Path.GetTempPath(), $"biscotti-text-tamper-{Guid.NewGuid()}");
        ProtectedMeetingText.ProtectorOverride = new PrefixMeetingTextProtector();
        try
        {
            var meeting = new Meeting { StartedUtc = DateTime.UtcNow, Title = "Protected meeting" };
            MeetingStore.Save(meeting);
            var jsonPath = Path.Combine(MeetingStore.MeetingDir(meeting.Id), "meeting.json");
            var stored = File.ReadAllBytes(jsonPath);
            stored[^1] ^= 0xFF;
            File.WriteAllBytes(jsonPath, stored);

            var error = Assert.Throws<MeetingTextStorageException>(() => MeetingStore.LoadMetadataOnly());
            Assert.Throws<MeetingTextStorageException>(() => new MeetingQueryService().SearchMeetings("Protected"));
            Assert.Throws<MeetingTextStorageException>(() => Biscotti.Core.MeetingHistorySearch.Search(
                MeetingStore.LoadAll().Select(Biscotti.Core.MeetingSearchDocument.FromMeeting), "Protected"));

            Assert.Contains("unprotect", error.Message);
            Assert.Equal(stored, File.ReadAllBytes(jsonPath));
        }
        finally
        {
            var dir = MeetingStore.RootOverride!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            MeetingStore.RootOverride = null;
            ProtectedMeetingText.ProtectorOverride = null;
        }
    }

    [Fact]
    public void MeetingStore_PreservesUnknownProtectedMarkdownVersion()
    {
        MeetingStore.RootOverride = Path.Combine(Path.GetTempPath(), $"biscotti-text-version-{Guid.NewGuid()}");
        ProtectedMeetingText.ProtectorOverride = new PrefixMeetingTextProtector();
        try
        {
            var dir = Path.Combine(MeetingStore.Root, Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            var markdownPath = Path.Combine(dir, "meeting.md");
            var original = Encoding.UTF8.GetBytes("BISCOTTI-DPAPI:2\nfuture protected bytes");
            File.WriteAllBytes(markdownPath, original);

            var error = Assert.Throws<MeetingTextStorageException>(() => ProtectedMeetingText.ReadAndMigrate(markdownPath));

            Assert.Contains("unsupported DPAPI envelope version", error.Message);
            Assert.Equal(original, File.ReadAllBytes(markdownPath));
        }
        finally
        {
            var dir = MeetingStore.RootOverride!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            MeetingStore.RootOverride = null;
            ProtectedMeetingText.ProtectorOverride = null;
        }
    }

    [Fact]
    public void MeetingStore_PreservesLegacyJsonWhenProtectedReplacementCannotBeVerified()
    {
        MeetingStore.RootOverride = Path.Combine(Path.GetTempPath(), $"biscotti-text-failure-{Guid.NewGuid()}");
        ProtectedMeetingText.ProtectorOverride = new PrefixMeetingTextProtector(failUnprotect: true);
        try
        {
            var meeting = new Meeting { StartedUtc = DateTime.UtcNow, Title = "Readable legacy data" };
            var dir = MeetingStore.MeetingDir(meeting.Id);
            Directory.CreateDirectory(dir);
            var jsonPath = Path.Combine(dir, "meeting.json");
            var original = JsonSerializer.SerializeToUtf8Bytes(meeting);
            File.WriteAllBytes(jsonPath, original);

            var error = Assert.Throws<MeetingTextStorageException>(() => MeetingStore.LoadMetadataOnly());

            Assert.Contains("unprotect", error.Message);
            Assert.Equal(original, File.ReadAllBytes(jsonPath));
            Assert.False(File.Exists(jsonPath + ".tmp"));
        }
        finally
        {
            var dir = MeetingStore.RootOverride!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            MeetingStore.RootOverride = null;
            ProtectedMeetingText.ProtectorOverride = null;
        }
    }

    private sealed class PrefixMeetingTextProtector(bool failUnprotect = false) : IMeetingTextProtector
    {
        private const byte Prefix = 0xA5;

        public byte[] Protect(byte[] plaintext) => [Prefix, .. SHA256.HashData(plaintext), .. plaintext];

        public byte[] Unprotect(byte[] ciphertext)
        {
            if (failUnprotect || ciphertext.Length < 33 || ciphertext[0] != Prefix)
                throw new CryptographicException("Test unprotect failure.");
            var plaintext = ciphertext[33..];
            if (!CryptographicOperations.FixedTimeEquals(ciphertext.AsSpan(1, 32), SHA256.HashData(plaintext)))
                throw new CryptographicException("Test protected content was tampered with.");
            return plaintext;
        }
    }

    [Fact]
    public void MeetingStore_RenderMarkdown_InterleavesTimestampedNotesAndTranscript()
    {
        var meeting = new Meeting { Title = "Timeline" };
        meeting.TimestampedNotes.Add(new TimestampedMeetingNote { Text = "decision", StartSeconds = 4 });
        meeting.Transcript.Add(new TranscriptLine { Speaker = "You", Text = "start", StartSeconds = 1, EndSeconds = 2 });
        meeting.Transcript.Add(new TranscriptLine { Speaker = "Speaker", Text = "response", StartSeconds = 6, EndSeconds = 7 });

        var markdown = MeetingStore.RenderMarkdown(meeting);
        var start = markdown.IndexOf("**You**: start", StringComparison.Ordinal);
        var note = markdown.IndexOf("**Note**: decision", StringComparison.Ordinal);
        var response = markdown.IndexOf("**Speaker**: response", StringComparison.Ordinal);

        Assert.True(start >= 0 && start < note && note < response);
    }

    [Fact]
    public void MeetingStore_LoadMetadataOnly_DoesNotMigrateLegacyAudio()
    {
        MeetingStore.RootOverride = Path.Combine(Path.GetTempPath(), $"biscotti-mcp-metadata-{Guid.NewGuid()}");
        try
        {
            var meeting = new Meeting { StartedUtc = DateTime.UtcNow, Title = "Metadata only" };
            MeetingStore.Save(meeting);
            var audioPath = Path.Combine(MeetingStore.MeetingDir(meeting.Id), "mic.wav");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-meeting-16k.wav"), audioPath);

            var loaded = MeetingStore.LoadMetadataOnly();

            Assert.Equal(meeting.Id, Assert.Single(loaded).Id);
            Assert.False(EncryptedAudioFile.IsEncrypted(audioPath));
            Assert.False(File.Exists(audioPath + ".enc-migrate"));
        }
        finally
        {
            var dir = MeetingStore.RootOverride!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            MeetingStore.RootOverride = null;
        }
    }

    [Fact]
    public void MeetingQueryService_SearchesMeetingsAndPagesTranscript()
    {
        var id = Guid.NewGuid();
        var meeting = new Meeting
        {
            Id = id,
            StartedUtc = DateTime.UtcNow,
            Title = "Quarterly launch",
            Summary = "Review the release checklist.",
        };
        meeting.Transcript.Add(new TranscriptLine { Speaker = "You", Text = "Budget is approved", StartSeconds = 2, EndSeconds = 3 });
        meeting.Transcript.Add(new TranscriptLine { Speaker = "Speaker 1", Text = "We can ship Friday", StartSeconds = 8, EndSeconds = 9 });
        var queries = new MeetingQueryService(() => new List<Meeting> { meeting });

        var search = queries.SearchMeetings("budget");
        var details = queries.GetMeeting(id.ToString());
        var firstPage = queries.GetTranscript(id.ToString(), offset: 0, limit: 1);
        var secondPage = queries.GetTranscript(id.ToString(), offset: 1, limit: 1);

        Assert.Equal(1, search.TotalMatches);
        Assert.Equal(id, Assert.Single(search.Meetings).Id);
        Assert.True(details.Found);
        Assert.Equal("Quarterly launch", details.Meeting!.Title);
        Assert.True(firstPage.HasMore);
        Assert.Equal("You", Assert.Single(firstPage.Lines!).Speaker);
        Assert.False(secondPage.HasMore);
        Assert.Equal("Speaker 1", Assert.Single(secondPage.Lines!).Speaker);
        Assert.Equal(8, secondPage.Lines![0].StartSeconds);
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
