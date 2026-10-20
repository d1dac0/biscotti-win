using Biscotti.Core.Audio;
using Biscotti.Core.Models;
using Biscotti.Core.Transcription;

namespace Biscotti.Tests;

public class LiveTranscriptionWorkerTests
{
    private const int BytesPerSecond = 16_000 * 2;
    private const int WindowBytes = 30 * BytesPerSecond;

    [Fact]
    public async Task Complete_DrainsNonOverlappingWindowsAndFinalPartialWindow()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"biscotti-live-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var sourcePath = Path.Combine(dir, "mic.wav");
        var sourcePcm = Enumerable.Range(0, 65 * BytesPerSecond)
            .Select(i => (byte)(i % 251))
            .ToArray();
        WriteWav(sourcePath, sourcePcm);

        var processedPcm = new List<byte[]>();
        var offsets = new List<double>();
        using var worker = new LiveTranscriptionWorker(
            sourcePath,
            Path.Combine(dir, "sys.wav"),
            () => (sourcePcm.Length, 0),
            (stream, speaker, _, offset) =>
            {
                stream.Position = 44;
                var pcm = new byte[stream.Length - 44];
                stream.ReadExactly(pcm);
                processedPcm.Add(pcm);
                offsets.Add(offset);
                return Task.FromResult(new List<TranscriptLine>
                {
                    new() { Speaker = speaker, Text = "window", StartSeconds = offset, EndSeconds = offset + 1 },
                });
            });
        var transcript = new List<TranscriptLine>();
        worker.LinesReady += lines => transcript.AddRange(lines);

        worker.Start();
        await worker.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { 0d, 30d, 60d }, offsets);
        Assert.Equal(new[] { WindowBytes, WindowBytes, 5 * BytesPerSecond }, processedPcm.Select(pcm => pcm.Length));
        Assert.Equal(sourcePcm, processedPcm.SelectMany(pcm => pcm).ToArray());
        Assert.Equal(offsets, transcript.Select(line => line.StartSeconds));
        Assert.All(transcript, line => Assert.Equal("You", line.Speaker));

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task PartialWindow_WaitsUntilRecordingCompletes()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"biscotti-live-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var sourcePath = Path.Combine(dir, "mic.wav");
        var writer = new WavStreamWriter(sourcePath);
        writer.Append(new byte[5 * BytesPerSecond]);
        var lines = new List<TranscriptLine>();
        using var worker = new LiveTranscriptionWorker(
            sourcePath,
            Path.Combine(dir, "sys.wav"),
            () => (5 * BytesPerSecond, 0),
            (stream, speaker, _, offset) => Task.FromResult(new List<TranscriptLine>
            {
                new() { Speaker = speaker, Text = "tail", StartSeconds = offset, EndSeconds = offset + 1 },
            }));
        worker.LinesReady += batch => lines.AddRange(batch);

        worker.Start();
        await Task.Delay(650);
        Assert.Empty(lines);

        await worker.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(10));
        writer.Close();

        Assert.Single(lines);
        Assert.Equal(0, lines[0].StartSeconds);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task EncryptedRecording_TranscribesMemoryWindowsWithoutWritingTemporaryWavs()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"biscotti-live-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var micPath = Path.Combine(dir, "mic.wav");
        var sysPath = Path.Combine(dir, "sys.wav");
        var pcm = Enumerable.Range(0, 4 * BytesPerSecond).Select(i => (byte)(i % 197)).ToArray();
        var protector = new TestKeyProtector();
        using (var mic = new EncryptedAudioWriter(micPath, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), 31, protector)) mic.Append(pcm);
        using (var sys = new EncryptedAudioWriter(sysPath, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), 32, protector)) { }
        byte[]? transcribed = null;
        using var worker = new LiveTranscriptionWorker(
            micPath, sysPath, () => (pcm.Length, 0),
            (stream, _, _, _) =>
            {
                stream.Position = 44;
                transcribed = new byte[stream.Length - 44];
                stream.ReadExactly(transcribed);
                return Task.FromResult(new List<TranscriptLine>());
            }, protector);

        worker.Start();
        await worker.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(pcm, transcribed);
        Assert.Equal(new[] { "mic.wav", "sys.wav" }, Directory.GetFiles(dir).Select(Path.GetFileName).Order().ToArray());
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task EncryptedRecording_DiscardsIncompleteTailWarnsAndDrainsAuthenticatedFrames()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"biscotti-live-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var micPath = Path.Combine(dir, "mic.wav");
        var sysPath = Path.Combine(dir, "sys.wav");
        var protector = new TestKeyProtector();
        var pcm = Enumerable.Range(0, EncryptedAudioWriter.FrameSize).Select(i => (byte)(i % 191)).ToArray();
        using (var mic = new EncryptedAudioWriter(micPath, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), 41, protector)) mic.Append(pcm);
        using (var sys = new EncryptedAudioWriter(sysPath, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), 42, protector)) { }
        using (var stream = new FileStream(micPath, FileMode.Append, FileAccess.Write)) stream.Write(new byte[] { 4, 0 });
        string? warning = null;
        byte[]? recovered = null;
        using var worker = new LiveTranscriptionWorker(
            micPath, sysPath, () => (pcm.Length + 2, 0),
            (stream, _, _, _) =>
            {
                stream.Position = 44;
                recovered = new byte[stream.Length - 44];
                stream.ReadExactly(recovered);
                return Task.FromResult(new List<TranscriptLine>());
            }, protector);
        worker.WarningReady += message => warning = message;

        worker.Start();
        await worker.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(pcm, recovered);
        Assert.Contains("incomplete audio frame", warning);
        Directory.Delete(dir, recursive: true);
    }

    private sealed class TestKeyProtector : IMeetingKeyProtector
    {
        public byte[] Protect(byte[] key) => key.Select(b => (byte)(b ^ 0x5A)).ToArray();
        public byte[] Unprotect(byte[] protectedKey) => protectedKey.Select(b => (byte)(b ^ 0x5A)).ToArray();
    }

    private static void WriteWav(string path, byte[] pcm)
    {
        var writer = new WavStreamWriter(path);
        writer.Append(pcm);
        writer.Close();
    }
}
