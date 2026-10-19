using Biscotti.Core.Models;
using Biscotti.Core.Transcription;
using Xunit;
using Xunit.Abstractions;

namespace Biscotti.Tests;

/// <summary>
/// Issue #10: a scripted, synthetic test-meeting fixture transcribed on the
/// CPU runtime that ships with Whisper.net.Runtime. The model used here is
/// the ggml-tiny checkpoint, downloaded once into a shared temp cache —
/// never committed (models are never vendored into git). An offline machine
/// cleanly skips; a wrong result fails. Uses no shared mutable Core state,
/// so it is safe alongside the parallel xunit test classes.
/// </summary>
public class TranscriptionFixtureTests
{
    private static readonly ModelStore.WhisperModel TinyModel =
        new("tiny", "Whisper Tiny (test-only checkpoint)", "ggml-tiny.bin", 77_700_000);

    private const string TinyUrl =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin";

    private static string CacheDir => Path.Combine(Path.GetTempPath(), "biscotti-test-models");

    private readonly ITestOutputHelper _output;

    public TranscriptionFixtureTests(ITestOutputHelper output) => _output = output;

    private static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-meeting-16k.wav");

    /// <summary>
    /// Standalone download into the shared temp cache (never git): reuses the
    /// repo's model-dir-and-rename discipline inline instead of the global
    /// ModelStore.ModelDirBackend seam, which xunit-parallel model tests race
    /// on (observed live in the full suite run).
    /// </summary>
    private static readonly SemaphoreSlim DownloadLock = new(1, 1);

    private static async Task<string> EnsureTinyModelAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(CacheDir);
        var target = Path.Combine(CacheDir, TinyModel.RepoFile);
        if (File.Exists(target) && new FileInfo(target).Length >= TinyModel.ApproxBytes * 0.9)
            return target;

        await DownloadLock.WaitAsync(ct);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var resp = await http.GetAsync(TinyUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var tmp = target + $".{Guid.NewGuid():N}.part";
            await using (var fs = await resp.Content.ReadAsStreamAsync(ct))
            await using (var outStream = File.Create(tmp))
                await fs.CopyToAsync(outStream, ct);
            File.Move(tmp, target, overwrite: true);
        }
        catch (Exception ex)
        {
            // network hiccups are an environment gap, not a product failure
            Skip.If(ex is HttpRequestException or TaskCanceledException or IOException,
                $"tiny model unavailable (offline?): {ex.Message}");
            throw;
        }
        finally
        {
            DownloadLock.Release();
        }
        return target;
    }

    [SkippableFact]
    public async Task FixtureTranscribesDeterministicallyWithExpectedKeywords()
    {
        var modelPath = await EnsureTinyModelAsync();

        var wavPath = FixturePath();
        var first = await TranscribeAsync(modelPath, wavPath);
        var second = await TranscribeAsync(modelPath, wavPath);

        // Determinism runs agree verbatim (issue #10 acceptance)
        var text1 = string.Join(" ", first.Select(l => l.Text.Trim()));
        var text2 = string.Join(" ", second.Select(l => l.Text.Trim()));

        _output.WriteLine($"run 1: {text1}");
        _output.WriteLine($"run 2: {text2}");
        Assert.Equal(Normalize(text1), Normalize(text2));

        // The scripted sentence is understood: its keywords land in the
        // transcript (normalized so whitespace/punctuation can't flake).
        var normalized = Normalize(text1);
        Assert.Contains("hello", normalized);
        Assert.Contains("meeting", normalized);
        Assert.Contains("transcription", normalized);
        Assert.Contains("test", normalized);
    }

    private static async Task<List<TranscriptLine>> TranscribeAsync(string modelPath, string wavPath)
    {
        using var transcriber = new WhisperTranscriber(modelPath);
        return await transcriber.TranscribeWavAsync(wavPath, speakerLabel: "Test", CancellationToken.None);
    }

    private static string Normalize(string text) =>
        string.Join(" ", text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
