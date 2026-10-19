using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Biscotti.Core.Models;
using Biscotti.Core.Transcription;
using Whisper.net.LibraryLoader;
using Xunit;

namespace Biscotti.Tests;

/// <summary>
/// Issue #10: a scripted spoken-meeting WAV fixture plus a transcriber
/// determinism test. The fixture is a committed 16 kHz mono PCM file
/// (scripted sentences with pauses, generated via SAPI); the model is the
/// tiny English GGML model, downloaded lazily into a machine-local test
/// cache exactly like the app's runtime downloads (never vendored in git).
/// Runs stay on the CPU runtime so the test is hardware-free.
/// </summary>
public class TranscriberDeterminismTests
{
    private const string ModelFile = "ggml-tiny.en.bin";
    private const long ModelFloorBytes = 60_000_000; // tiny.en is ~75 MB

    private static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BiscottiWin.TestCache", "Models");

    private static string CachedModelPath => Path.Combine(CacheDir, ModelFile);

    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "test-meeting.wav");

    static TranscriberDeterminismTests()
    {
        // Write-once, before any WhisperFactory is built: keep the CPU
        // runtime even on machines with NVIDIA GPUs, so results are
        // hardware-independent (the CI runner has no GPU).
        var existing = RuntimeOptions.RuntimeLibraryOrder;
        if (existing is not IReadOnlyList<RuntimeLibrary> list || list.Count != 1 || list[0] != RuntimeLibrary.Cpu)
        {
            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
        }
    }

    /// <summary>
    /// Transcribing the fixture is deterministic: two independent runs of
    /// the whole pipeline (fresh transcriber, model reload, WAV decode)
    /// produce the same lines, and the scripted keywords come through.
    /// </summary>
    [Fact]
    public async Task TranscribeWav_Fixture_IsDeterministicAndCarriesKeywords()
    {
        var path = await EnsureModelDownloadedAsync();

        var first = await TranscribeAsync(path);
        var second = await TranscribeAsync(path);

        Assert.NotEmpty(first);
        var text1 = string.Join(" ", first.Select(l => l.Text));
        var text2 = string.Join(" ", second.Select(l => l.Text));
        Assert.Equal(text1, text2);

        foreach (var keyword in new[] { "meeting", "quarterly", "report", "friday" })
            Assert.Contains(keyword, text1.ToLowerInvariant());
    }

    /// <summary>
    /// The fixture file itself must be healthy and small enough to commit:
    /// canonical 16 kHz mono PCM, spoken-length, under half a megabyte.
    /// </summary>
    [Fact]
    public void Fixture_IsSmallCanonicalPcmWav()
    {
        Assert.True(File.Exists(FixturePath), "fixtures/test-meeting.wav must be committed");
        var bytes = File.ReadAllBytes(FixturePath);
        Assert.InRange(bytes.Length, 32_768, 512_000);
        Assert.Equal("RIFF"u8.ToArray(), bytes[..4]);
        Assert.Equal("WAVE"u8.ToArray(), bytes[8..12]);
        Assert.Equal(16000u, BitConverter.ToUInt32(bytes, 24));
        Assert.Equal((ushort)1, BitConverter.ToUInt16(bytes, 22)); // mono
        Assert.Equal((ushort)16, BitConverter.ToUInt16(bytes, 34)); // bits
    }

    private static async Task<List<TranscriptLine>> TranscribeAsync(string modelPath)
    {
        using var transcriber = new WhisperTranscriber(modelPath);
        return await transcriber.TranscribeWavAsync(FixturePath, "You");
    }

    private static async Task<string> EnsureModelDownloadedAsync()
    {
        var target = CachedModelPath;
        Directory.CreateDirectory(CacheDir);

        if (File.Exists(target) && new FileInfo(target).Length >= ModelFloorBytes)
            return target;

        // Present but incomplete: park it the way ModelStore does (issue #32
        // pattern) so the download starts fresh.
        if (File.Exists(target))
            File.Move(target, target + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}");

        var tmp = target + ".part";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{ModelFile}");
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None);
        resp.EnsureSuccessStatusCode();

        await using (var outStream = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        {
            await using var inStream = resp.Content.ReadAsStream(CancellationToken.None);
            await inStream.CopyToAsync(outStream, CancellationToken.None);
        }

        // Rename after the write stream is closed. A freshly written file
        // can be briefly held by AV/indexer scans on Windows; retry the
        // rename a few times before giving up.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(tmp, target, overwrite: true);
                break;
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
            }
        }
        return target;
    }
}
