using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Biscotti.Core.Transcription;

/// <summary>
/// Lazily downloads and unpacks the Whisper GPU (CUDA) runtime when the
/// user opts in (issue #20). Nothing is vendored into git: files land in
/// %LOCALAPPDATA%\BiscottiWin\runtimes\cuda\win-x64, exactly the layout
/// Whisper.net's NativeLibraryLoader probes under RuntimeOptions.LibraryPath.
///
/// Sources, pinned by version:
///  - whisper.net.runtime.cuda.windows  (NuGet, MIT; whisper.cpp CUDA build)
///  - nvidia-cuda-runtime               (NVIDIA's official Windows wheel)
///  - nvidia-cublas                     (NVIDIA's official Windows wheel)
/// The NVIDIA wheels are the CUDA redistributable channel: cudart64_13,
/// cublas64_13, cublasLt64_13 and nvblas64_13 land next to
/// ggml-cuda-whisper.dll and satisfy its hard imports. The display driver
/// provides nvcuda.dll. A driver supporting CUDA 13+ is required (any
/// current NVIDIA driver).
/// </summary>
public class GpuRuntimeStore
{
    public record RuntimePackage(string PackageId, string Version, string DirInPackage, string[] Files);

    /// <summary>Pinned source set — bump together with the Whisper.net package reference in the csproj.</summary>
    public static readonly RuntimePackage[] Sources =
    [
        new("whisper.net.runtime.cuda.windows", "1.9.0", "build/win-x64",
            ["whisper.dll", "ggml-whisper.dll", "ggml-base-whisper.dll", "ggml-cpu-whisper.dll", "ggml-cuda-whisper.dll"]),
        new("nvidia-cuda-runtime", "13.1.80", "nvidia/cu13/bin/x86_64", ["cudart64_13.dll"]),
        new("nvidia-cublas", "13.1.1.3", "nvidia/cu13/bin/x86_64",
            ["cublas64_13.dll", "cublasLt64_13.dll", "nvblas64_13.dll"]),
    ];

    /// <summary>
    /// Whisper.net reads RuntimeOptions.LibraryPath as a *file* path and
    /// probes runtimes/&lt;variant&gt;/&lt;rid&gt; under its parent directory.
    /// The stored path is therefore a marker file inside the data root, which
    /// makes the loader probe BiscottiWin\runtimes\cuda\win-x64.
    /// </summary>
    public const string RuntimeMarkerFile = "runtime-root.marker";

    private readonly string _root;

    /// <param name="rootOverride">tests only; default is %LOCALAPPDATA%\BiscottiWin</param>
    public GpuRuntimeStore(string? rootOverride = null)
    {
        _root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BiscottiWin");
    }

    public string TargetDir => Path.Combine(_root, "runtimes", "cuda", "win-x64");

    public string LibraryPathMarker => Path.Combine(_root, RuntimeMarkerFile);

    /// <summary>True when every hard-import DLL of the CUDA ggml build is in place.</summary>
    public bool IsInstalled()
    {
        foreach (var pkg in Sources)
            foreach (var f in pkg.Files)
                if (!File.Exists(Path.Combine(TargetDir, f)) || new FileInfo(Path.Combine(TargetDir, f)).Length == 0)
                    return false;
        return true;
    }

    /// <summary>
    /// Download + extract all packages. Crash-safe by construction: files are
    /// extracted into a staging sibling dir and only moved into the target
    /// dir after the full inventory succeeded; on any failure the staging dir
    /// is removed so the next attempt starts clean.
    /// </summary>
    public async Task InstallAsync(IProgress<(double pct, string phase)>? progress,
        CancellationToken ct = default)
    {
        if (IsInstalled()) { progress?.Report((100, "GPU runtime already installed.")); return; }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };

        // resolve one URL + byte size per package up front for progress math
        var plan = new List<(RuntimePackage pkg, string url, long bytes)>();
        foreach (var pkg in Sources)
        {
            var (url, bytes) = await ResolveUrlAndSizeAsync(http, pkg, ct);
            plan.Add((pkg, url, bytes));
        }
        long total = Math.Max(1, plan.Sum(p => p.bytes));

        var target = TargetDir;
        var stage = target + ".staging-" + Guid.NewGuid().ToString("N")[..8];
        Directory.CreateDirectory(stage);
        try
        {
            long done = 0;
            for (int i = 0; i < plan.Count; i++)
            {
                var (pkg, url, bytes) = plan[i];
                progress?.Report((done * 100.0 / total, $"Downloading GPU runtime ({i + 1}/{plan.Count})…"));
                ct.ThrowIfCancellationRequested();
                await using var stream = await http.GetStreamAsync(url, ct);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                foreach (var f in pkg.Files)
                    await ExtractEntryAsync(archive, pkg.DirInPackage, f, stage, ct);
                done += bytes;
                progress?.Report((done * 100.0 / total, "Downloading GPU runtime…"));
            }

            // run the full inventory only into the live dir if all landed with real bytes
            foreach (var item in plan)
                foreach (var f in item.pkg.Files)
                    if (!File.Exists(Path.Combine(stage, f)) || new FileInfo(Path.Combine(stage, f)).Length == 0)
                        throw new IOException($"GPU runtime extracts incomplete: {f}");

            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(stage))
                File.Move(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            File.WriteAllText(LibraryPathMarker,
                "# marks the CUDA runtime root for Whisper.net LibraryPath (see GpuRuntimeStore)\n");
        }
        finally
        {
            try { Directory.Delete(stage, recursive: true); } catch { }
        }
    }

    /// <summary>One archive entry → one staged file. Testable without network.</summary>
    internal static async Task ExtractEntryAsync(ZipArchive archive, string dirInPackage, string fileName,
        string stageDir, CancellationToken ct)
    {
        var entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.Replace('\\', '/').Equals($"{dirInPackage}/{fileName}", StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"{fileName} missing from archive");
        var outPath = Path.Combine(stageDir, fileName);
        await using var es = entry.Open();
        await using var outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
        await es.CopyToAsync(outFs, ct);
    }

    private static async Task<(string url, long bytes)> ResolveUrlAndSizeAsync(
        HttpClient http, RuntimePackage pkg, CancellationToken ct)
    {
        if (pkg.PackageId.StartsWith("whisper.net", StringComparison.OrdinalIgnoreCase))
        {
            var url = $"https://api.nuget.org/v3-flatcontainer/{pkg.PackageId}/{pkg.Version}/{pkg.PackageId}.{pkg.Version}.nupkg";
            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await http.SendAsync(head, ct);
            resp.EnsureSuccessStatusCode();
            return (url, resp.Content.Headers.ContentLength.GetValueOrDefault(0));
        }
        // NVIDIA: official Windows wheels on NVIDIA's PyPI channel
        var json = await http.GetStringAsync($"https://pypi.org/pypi/{pkg.PackageId}/{pkg.Version}/json", ct);
        var (wheelUrl, size) = WheelUrlFromPypiJson(json);
        return wheelUrl is null
            ? throw new InvalidOperationException($"No Windows wheel found for {pkg.PackageId} {pkg.Version}")
            : (wheelUrl, size);
    }

    /// <summary>Pure JSON → (win_amd64 wheel url, byte size) resolver; testable without network.</summary>
    internal static (string? url, long bytes) WheelUrlFromPypiJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var u in doc.RootElement.GetProperty("urls").EnumerateArray())
        {
            var name = u.GetProperty("filename").GetString() ?? "";
            if (!name.Contains("win_amd64", StringComparison.OrdinalIgnoreCase)) continue;
            return (u.GetProperty("url").GetString(), u.TryGetProperty("size", out var s) ? s.GetInt64() : 0);
        }
        return (null, 0);
    }

    /// <summary>
    /// Loads cudart64_13.dll by absolute path and keeps the handle alive, so
    /// Whisper.net's bare-name cudart64_13 probe (CudaHelper.IsCudaAvailable)
    /// resolves through the already-loaded module. Must run before any
    /// WhisperFactory is created.
    /// </summary>
    public void PrimeCudaRuntime()
    {
        try
        {
            var cudart = Path.Combine(TargetDir, "cudart64_13.dll");
            if (File.Exists(cudart)) NativeLibrary.Load(cudart);
        }
        catch
        {
            // If GPU fails later at whisper's probe, it falls back to CPU silently (issue #3 lesson).
        }
    }
}
