using System.IO.Compression;
using Biscotti.Core.Transcription;
using Whisper.net.LibraryLoader;
using Xunit;

namespace Biscotti.Tests;

/// <summary>Issue #20: NVIDIA detection and lazy GPU runtime plumbing.</summary>
public class GpuRuntimeTests
{
    [Fact]
    public void DetectParsesNvidiaSmiOutput()
    {
        var saved = (Func<string>)(() =>
            "GPU 0: NVIDIA GeForce RTX 3080 (UUID: GPU-abcd1234)\n" +
            "GPU 1: NVIDIA RTX A4000 (UUID: GPU-ef567890)\n");
        var gpu = GpuDetector.DetectWithProbe(saved);
        Assert.NotNull(gpu);
        Assert.Equal("NVIDIA GeForce RTX 3080", gpu!.Description);
    }

    [Fact]
    public void DetectStripsUuidFromDescription()
    {
        var single = (Func<string>)(() => "GPU 0: NVIDIA RTX A10 (UUID: GPU-xx)\n");
        Assert.Equal("NVIDIA RTX A10", GpuDetector.DetectWithProbe(single)!.Description);
    }

    [Fact]
    public void DetectReturnsNullWhenNoNvidia()
    {
        Assert.Null(GpuDetector.DetectWithProbe(() => ""));
        Assert.Null(GpuDetector.DetectWithProbe(() => "nvidia-smi: not found"));
        Assert.Null(GpuDetector.DetectWithProbe(() => throw new InvalidOperationException()));
    }

    [Fact]
    public void WheelUrlParserPicksWinAmd64()
    {
        const string json = """
            {"urls":[
              {"filename":"nvidia_cublas-13.1.1.3-py3-none-manylinux2014_x86_64.whl","url":"https://x/linux.whl","size":100},
              {"filename":"nvidia_cublas-13.1.1.3-py3-none-win_amd64.whl","url":"https://x/windows.whl","size":404000000}
            ]}
            """;
        var (url, bytes) = GpuRuntimeStore.WheelUrlFromPypiJson(json);
        Assert.Equal("https://x/windows.whl", url);
        Assert.Equal(404_000_000, bytes);
    }

    [Fact]
    public void WheelUrlParserReturnsNullWhenOnlyLinux()
    {
        const string json = """{"urls":[{"filename":"pkg-py3-none-any.whl","url":"https://x/any.whl","size":5}]}""";
        var (url, _) = GpuRuntimeStore.WheelUrlFromPypiJson(json);
        Assert.Null(url);
    }

    [Fact]
    public void SelectOrderGpuRequiresInstall()
    {
        Assert.Equal([RuntimeLibrary.Cuda, RuntimeLibrary.Cpu],
            WhisperRuntimeConfigurator.SelectOrder("gpu", gpuFilesInstalled: true));
        // not installed -> silent CPU (issue #3 lesson: never crash on GPU)
        Assert.Equal([RuntimeLibrary.Cpu],
            WhisperRuntimeConfigurator.SelectOrder("gpu", gpuFilesInstalled: false));
        Assert.Equal([RuntimeLibrary.Cpu],
            WhisperRuntimeConfigurator.SelectOrder("cpu", gpuFilesInstalled: true));
        Assert.Equal([RuntimeLibrary.Cpu],
            WhisperRuntimeConfigurator.SelectOrder(null, gpuFilesInstalled: true));
    }

    [Fact]
    public void StoreLayoutMatchesWhisperNetProbePaths()
    {
        var store = new GpuRuntimeStore(Path.Combine(Path.GetTempPath(), $"gpu-root-{Guid.NewGuid():N}"));
        try
        {
            // Whisper.net probes <LibraryPath-dir>/runtimes/cuda/win-x64
            var markerDir = Path.GetDirectoryName(store.LibraryPathMarker)!;
            Assert.Equal(Path.Combine(markerDir, "runtimes", "cuda", "win-x64"), store.TargetDir);
            Assert.False(store.IsInstalled()); // nothing downloaded yet
        }
        finally
        {
            var dir = Path.GetDirectoryName(store.LibraryPathMarker)!;
            if (Directory.Exists(dir))
                Directory.Delete(Path.Combine(dir, "runtimes"), recursive: true);
        }
    }

    [Fact]
    public async Task ExtractEntryUnpacksNupkgStyleLayout()
    {
        // build a zip that mirrors Whisper.net.Runtime.Cuda.Windows layout
        var root = Path.Combine(Path.GetTempPath(), $"gpu-zip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var zipPath = Path.Combine(root, "fake.nupkg");
            using (var fs = File.Create(zipPath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("build/win-x64/ggml-cuda-whisper.dll");
                await using var w = entry.Open();
                var payload = new byte[] { 1, 2, 3 };
                await w.WriteAsync(payload);
            }
            await using var stream = File.OpenRead(zipPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var stage = Path.Combine(root, "stage");
            Directory.CreateDirectory(stage);
            await GpuRuntimeStore.ExtractEntryAsync(archive, "build/win-x64", "ggml-cuda-whisper.dll", stage, CancellationToken.None);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(stage, "ggml-cuda-whisper.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractEntryThrowsWhenEntryMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gpu-zip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var zipPath = Path.Combine(root, "fake.nupkg");
            using (var fs = File.Create(zipPath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                zip.CreateEntry("build/win-x64/other.dll");
            }
            await using var stream = File.OpenRead(zipPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var stage = Path.Combine(root, "stage");
            Directory.CreateDirectory(stage);
            await Assert.ThrowsAsync<FileNotFoundException>(()
                => GpuRuntimeStore.ExtractEntryAsync(archive, "build/win-x64", "whisper.dll", stage, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
