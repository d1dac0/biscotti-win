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
            (path, speaker, _, offset) =>
            {
                using var stream = File.OpenRead(path);
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
            (_, speaker, _, offset) => Task.FromResult(new List<TranscriptLine>
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

    private static void WriteWav(string path, byte[] pcm)
    {
        var writer = new WavStreamWriter(path);
        writer.Append(pcm);
        writer.Close();
    }
}
