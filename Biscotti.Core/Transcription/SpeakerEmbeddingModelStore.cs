using System.Net.Http;
using System.Security.Cryptography;

namespace Biscotti.Core.Transcription;

/// <summary>Downloads the pinned WeSpeaker embedding model on demand.</summary>
public static class SpeakerEmbeddingModelStore
{
    public const string FileName = "wespeaker_en_voxceleb_resnet34.onnx";
    public const long ModelSize = 26_534_365;
    public const string Sha256 = "5ef208a9da1453335308a6b6f4e6dfbd7e183a38b604de0a57664f45d257fe94";
    public const string DownloadUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/wespeaker_en_voxceleb_resnet34.onnx";

    public static string ModelPath => Path.Combine(ModelStore.ModelDir, FileName);

    public static bool IsInstalled() => IsValidFile(ModelPath);

    public static bool IsValidFile(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != ModelSize) return false;
        using var stream = File.OpenRead(path);
        return VerifySha256(SHA256.HashData(stream));
    }

    internal static bool VerifySha256(ReadOnlySpan<byte> hash) =>
        Convert.ToHexString(hash).Equals(Sha256, StringComparison.OrdinalIgnoreCase);

    public static async Task<string> EnsureDownloadedAsync(
        IProgress<(double pct, string phase)>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ModelStore.ModelDir);
        var target = ModelPath;
        if (IsValidFile(target)) return target;
        if (File.Exists(target))
            File.Move(target, target + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}");

        var temp = target + ".part";
        if (File.Exists(temp)) File.Delete(temp);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, DownloadUrl);
        request.Headers.UserAgent.ParseAdd("BiscottiWin/1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength.GetValueOrDefault(ModelSize);
        await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        {
            var buffer = new byte[1 << 18];
            long written = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                written += read;
                progress?.Report((written * 100.0 / total, "Downloading speaker recognition model…"));
            }
        }

        if (!IsValidFile(temp))
        {
            File.Delete(temp);
            throw new InvalidDataException("The speaker recognition model failed its SHA-256 integrity check.");
        }

        File.Move(temp, target, overwrite: true);
        progress?.Report((100, "Speaker recognition model is ready."));
        return target;
    }
}
