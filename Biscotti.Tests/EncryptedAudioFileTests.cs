using System.Security.Cryptography;
using Biscotti.Core.Audio;

namespace Biscotti.Tests;

public sealed class EncryptedAudioFileTests
{
    [Fact]
    public void WriterReader_RoundTripsFramesAndRandomRanges_WithoutPlaintextOnDisk()
    {
        var dir = NewDirectory();
        var path = Path.Combine(dir, "mic.wav");
        var pcm = Enumerable.Range(0, EncryptedAudioWriter.FrameSize * 3 + 917)
            .Select(i => (byte)(i % 239)).ToArray();
        var key = RandomNumberGenerator.GetBytes(32);
        using (var writer = new EncryptedAudioWriter(path, key, 123, new TestKeyProtector())) writer.Append(pcm);

        var bytes = File.ReadAllBytes(path);
        Assert.StartsWith("BISCWAV1", System.Text.Encoding.ASCII.GetString(bytes, 0, 8));
        Assert.False(ContainsSequence(bytes, pcm));
        Assert.False(ContainsSequence(bytes, key));
        using (var reader = new EncryptedAudioReader(path, new TestKeyProtector()))
        {
            Assert.Equal(pcm.Length, reader.PcmLength);
            Assert.Equal(pcm[4000..9000], reader.ReadPcmRange(4000, 5000));
            Assert.Equal(pcm, reader.ReadPcmRange(0, pcm.Length));
        }
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Reader_RejectsModifiedFrameTag()
    {
        var dir = NewDirectory();
        var path = Path.Combine(dir, "mic.wav");
        using (var writer = new EncryptedAudioWriter(path, RandomNumberGenerator.GetBytes(32), 7, new TestKeyProtector()))
            writer.Append(new byte[1000]);
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0x40;
        File.WriteAllBytes(path, bytes);

        using (var reader = new EncryptedAudioReader(path, new TestKeyProtector()))
            Assert.Throws<AuthenticationTagMismatchException>(() => reader.ReadPcmRange(0, 1000));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Reader_RejectsUnsupportedHeaderVersion()
    {
        var dir = NewDirectory();
        var path = Path.Combine(dir, "mic.wav");
        using (var writer = new EncryptedAudioWriter(path, RandomNumberGenerator.GetBytes(32), 9, new TestKeyProtector()))
            writer.Append(new byte[256]);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            stream.Position = 8;
            stream.WriteByte(2);
        }

        Assert.Throws<InvalidDataException>(() => new EncryptedAudioReader(path, new TestKeyProtector()));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void PlaybackStream_DecryptsAuthenticatedPcmOnReadWithoutCreatingPlaintextFile()
    {
        var dir = NewDirectory();
        var path = Path.Combine(dir, "mic.wav");
        var pcm = Enumerable.Range(0, 7000).Select(i => (byte)(i % 211)).ToArray();
        using (var writer = new EncryptedAudioWriter(path, RandomNumberGenerator.GetBytes(32), 29, new TestKeyProtector()))
            writer.Append(pcm);

        using (var wav = EncryptedAudioPlayback.OpenWavStream(path, new TestKeyProtector()))
        {
            var bytes = new byte[wav.Length];
            wav.ReadExactly(bytes);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal(pcm, bytes[44..]);
        }
        Assert.Equal(new[] { "mic.wav" }, Directory.GetFiles(dir).Select(Path.GetFileName));
        Assert.False(ContainsSequence(File.ReadAllBytes(path), pcm));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void PlaybackStream_SurfacesDpapiKeyFailureAsReadableError()
    {
        var dir = NewDirectory();
        var path = Path.Combine(dir, "sys.wav");
        using (var writer = new EncryptedAudioWriter(path, RandomNumberGenerator.GetBytes(32), 31, new TestKeyProtector()))
            writer.Append(new byte[128]);

        var error = Assert.Throws<CryptographicException>(() => EncryptedAudioPlayback.OpenWavStream(path, new FailingProtector()));
        Assert.Contains("Could not unlock", error.Message);
        Assert.Contains("sys channel", error.Message);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void PlaybackStream_ReportsMissingChannelWithoutThrowingAnUnhelpfulPathError()
    {
        var dir = NewDirectory();
        var path = Path.Combine(dir, "sys.wav");

        var error = Assert.Throws<FileNotFoundException>(() => EncryptedAudioPlayback.OpenWavStream(path, new TestKeyProtector()));
        Assert.Contains("No saved audio was found", error.Message);
        Assert.Contains("sys channel", error.Message);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void PlaybackStream_RejectsTamperedAudioWhenTheFrameIsRead()
    {
        var dir = NewDirectory();
        var path = Path.Combine(dir, "mic.wav");
        using (var writer = new EncryptedAudioWriter(path, RandomNumberGenerator.GetBytes(32), 37, new TestKeyProtector()))
            writer.Append(new byte[1000]);
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0x20;
        File.WriteAllBytes(path, bytes);

        using (var wav = EncryptedAudioPlayback.OpenWavStream(path, new TestKeyProtector()))
        {
            wav.Position = 44;
            Assert.Throws<AuthenticationTagMismatchException>(() => wav.Read(new byte[1000], 0, 1000));
        }
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Reader_KeepsPriorFramesAndReportsIncompleteTail()
    {
        var dir = NewDirectory();
        var path = Path.Combine(dir, "mic.wav");
        using (var writer = new EncryptedAudioWriter(path, RandomNumberGenerator.GetBytes(32), 17, new TestKeyProtector()))
            writer.Append(new byte[EncryptedAudioWriter.FrameSize]);
        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write)) stream.Write(new byte[] { 20, 0, 0 });

        using (var reader = new EncryptedAudioReader(path, new TestKeyProtector()))
        {
            Assert.Equal(EncryptedAudioWriter.FrameSize, reader.PcmLength);
            Assert.True(reader.HasTruncatedTail);
        }
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Migration_ConvertsBothLegacyFilesAndCanRepeat()
    {
        var dir = NewDirectory();
        var micPath = Path.Combine(dir, "mic.wav");
        var sysPath = Path.Combine(dir, "sys.wav");
        var mic = Enumerable.Range(0, 9001).Select(i => (byte)(i % 233)).ToArray();
        var sys = Enumerable.Range(0, 12003).Select(i => (byte)(i % 227)).ToArray();
        WriteWav(micPath, mic);
        WriteWav(sysPath, sys);

        MeetingAudioMigration.EnsureEncrypted(dir, new TestKeyProtector());
        MeetingAudioMigration.EnsureEncrypted(dir, new TestKeyProtector());

        using (var micReader = new EncryptedAudioReader(micPath, new TestKeyProtector()))
        using (var sysReader = new EncryptedAudioReader(sysPath, new TestKeyProtector()))
        {
            Assert.NotEqual(micReader.ChannelId, sysReader.ChannelId);
            Assert.Equal(mic, micReader.ReadPcmRange(0, mic.Length));
            Assert.Equal(sys, sysReader.ReadPcmRange(0, sys.Length));
        }
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void MigrationFailure_PreservesOriginalPlaintext()
    {
        var dir = NewDirectory();
        var path = Path.Combine(dir, "mic.wav");
        WriteWav(path, new byte[] { 1, 2, 3, 4 });
        var before = File.ReadAllBytes(path);

        var error = Assert.Throws<IOException>(() => MeetingAudioMigration.EnsureEncrypted(dir, new FailingProtector()));
        Assert.IsType<CryptographicException>(error.InnerException);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".enc-migrate"));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Migration_ResumesWhenOnlyOneChannelWasAlreadyEncrypted()
    {
        var dir = NewDirectory();
        var micPath = Path.Combine(dir, "mic.wav");
        var sysPath = Path.Combine(dir, "sys.wav");
        var key = RandomNumberGenerator.GetBytes(32);
        var mic = Enumerable.Range(0, 7000).Select(i => (byte)(i % 199)).ToArray();
        var sys = Enumerable.Range(0, 5000).Select(i => (byte)(i % 193)).ToArray();
        using (var writer = new EncryptedAudioWriter(micPath, key, 101, new TestKeyProtector())) writer.Append(mic);
        WriteWav(sysPath, sys);

        MeetingAudioMigration.EnsureEncrypted(dir, new TestKeyProtector());

        using (var micReader = new EncryptedAudioReader(micPath, new TestKeyProtector()))
        using (var sysReader = new EncryptedAudioReader(sysPath, new TestKeyProtector()))
        {
            Assert.Equal(mic, micReader.ReadPcmRange(0, mic.Length));
            Assert.Equal(sys, sysReader.ReadPcmRange(0, sys.Length));
            Assert.Equal(micReader.CopyMeetingKey(), sysReader.CopyMeetingKey());
        }
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void ReaderSurfacesKeyUnprotectFailure()
    {
        var dir = NewDirectory();
        var path = Path.Combine(dir, "mic.wav");
        using (var writer = new EncryptedAudioWriter(path, RandomNumberGenerator.GetBytes(32), 23, new TestKeyProtector()))
            writer.Append(new byte[100]);

        Assert.Throws<CryptographicException>(() => new EncryptedAudioReader(path, new FailingProtector()));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void DpapiCurrentUser_ProtectsAndRestoresMeetingKey()
    {
        if (!OperatingSystem.IsWindows()) return;
        var key = RandomNumberGenerator.GetBytes(32);
        var protector = new DpapiMeetingKeyProtector();
        var wrapped = protector.Protect(key);
        Assert.False(ContainsSequence(wrapped, key));
        Assert.Equal(key, protector.Unprotect(wrapped));
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"biscotti-encrypted-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool ContainsSequence(byte[] source, byte[] sequence)
    {
        for (var i = 0; i <= source.Length - sequence.Length; i++)
            if (source.AsSpan(i, sequence.Length).SequenceEqual(sequence)) return true;
        return false;
    }

    private static void WriteWav(string path, byte[] pcm)
    {
        var writer = new WavStreamWriter(path);
        writer.Append(pcm);
        writer.Close();
    }

    private sealed class TestKeyProtector : IMeetingKeyProtector
    {
        public byte[] Protect(byte[] key) => key.Select(b => (byte)(b ^ 0xA5)).ToArray();
        public byte[] Unprotect(byte[] protectedKey) => protectedKey.Select(b => (byte)(b ^ 0xA5)).ToArray();
    }

    private sealed class FailingProtector : IMeetingKeyProtector
    {
        public byte[] Protect(byte[] key) => key.ToArray();
        public byte[] Unprotect(byte[] protectedKey) => throw new CryptographicException("Test DPAPI failure.");
    }
}
