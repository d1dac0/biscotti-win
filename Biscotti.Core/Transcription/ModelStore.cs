using System.Net.Http;
using System.Security.Cryptography;

namespace Biscotti.Core.Transcription;

/// <summary>
/// Downloads and caches Whisper GGML models from Hugging Face into
/// %LOCALAPPDATA%\BiscottiWin\Models. Downloads are streamed to .part files
/// and renamed after completion so a killed download can be retried safely.
/// </summary>
public static class ModelStore
{
    public record WhisperModel(string Id, string DisplayName, string RepoFile, long ApproxBytes);

    public static readonly WhisperModel LargeV3Turbo = new(
        "large-v3-turbo", "Whisper Large v3 Turbo (best, ~1.6 GB)",
        "ggml-large-v3-turbo.bin", 1_620_000_000);

    public static readonly WhisperModel Small = new(
        "small", "Whisper Small (fast, ~466 MB)",
        "ggml-small.bin", 466_000_000);

    public static readonly List<WhisperModel> All = new() { LargeV3Turbo, Small };

    // Test seam: lets unit tests redirect the model directory to a temp path.
    internal static Func<string> ModelDirBackend = () => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BiscottiWin", "Models");

    public static string ModelDir => ModelDirBackend();

    public static string ModelPath(WhisperModel m) => Path.Combine(ModelDir, m.RepoFile);

    /// <summary>
    /// The file must be plausibly complete, not merely present: a truncated
    /// mid-write file must not pass as "downloaded" (issue #32). The 10%
    /// tolerance keeps the approximate ApproxBytes constants from
    /// false-flagging a legit file.
    /// </summary>
    public static bool IsDownloaded(WhisperModel m)
    {
        var path = ModelPath(m);
        if (!File.Exists(path)) return false;
        long floor = Math.Max(1_000_000, (long)(m.ApproxBytes * 0.9));
        return new FileInfo(path).Length >= floor;
    }

    /// <summary>
    /// Issue #32: when a present-but-incomplete model file blocks the
    /// download path, move it aside (keep a timestamped copy) so the normal
    /// download flow can place a fresh file — the Download button becomes
    /// the repair path.
    /// </summary>
    internal static void MoveCorruptAside(WhisperModel m)
    {
        var target = ModelPath(m);
        if (File.Exists(target))
            File.Move(target, target + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}");
    }

    private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    public static async Task<WhisperModel?> EnsureDownloadedAsync(
        WhisperModel m, IProgress<(double pct, string phase)>? progress, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        return await DownloadAsync(m, http, AvailableDiskBytes, progress, ct);
    }

    public static long AvailableDiskBytes() => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(ModelDir))!).AvailableFreeSpace;

    internal static async Task<WhisperModel?> DownloadAsync(WhisperModel m, HttpClient http,
        Func<long> availableBytes, IProgress<(double pct, string phase)>? progress, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(ModelDir);
        if (IsDownloaded(m)) return m;
        var target = ModelPath(m);
        var tmp = target + ".part";
        long existing = File.Exists(tmp) ? new FileInfo(tmp).Length : 0;
        // Allow a full replacement if the server does not support resuming.
        if (availableBytes() < m.ApproxBytes + 100_000_000)
            throw new IOException($"Not enough disk space. Free at least {(m.ApproxBytes + 100_000_000) / 1_000_000.0:0} MB before retrying.");
        MoveCorruptAside(m);

        using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + m.RepoFile);
        if (existing > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(tmp);
            throw new InvalidDataException("The partial model could not be resumed. Retry to start a fresh download.");
        }
        resp.EnsureSuccessStatusCode();

        var resume = resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (resume && resp.Content.Headers.ContentRange?.From != existing)
            throw new InvalidDataException("The model server returned an invalid download range. Retry the download.");
        if (!resume) existing = 0;

        long total = resp.Content.Headers.ContentLength.GetValueOrDefault() + existing;
        progress?.Report((0, $"Downloading {m.DisplayName}…"));
        await using (var outStream = new FileStream(tmp, resume ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var inStream = await resp.Content.ReadAsStreamAsync(ct))
        {
            var buf = new byte[1 << 20];
            long done = existing;
            int read;
            while ((read = await inStream.ReadAsync(buf, ct)) > 0)
            {
                await outStream.WriteAsync(buf.AsMemory(0, read), ct);
                done += read;
                if (total > 0)
                    progress?.Report((done * 100.0 / total, $"Downloading {m.DisplayName}…"));
            }
        }
        ct.ThrowIfCancellationRequested();
        if (new FileInfo(tmp).Length < Math.Max(1_000_000, (long)(m.ApproxBytes * 0.9)) ||
            (total > 0 && new FileInfo(tmp).Length != total))
        {
            File.Delete(tmp);
            throw new InvalidDataException("The model download was incomplete. Retry to download a fresh copy.");
        }
        File.Move(tmp, target, overwrite: true);
        return m;
    }
}
