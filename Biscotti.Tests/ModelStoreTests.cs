using Biscotti.Core.Transcription;
using Xunit;

namespace Biscotti.Tests;

/// <summary>Issue #32: a present-but-truncated model file must fail the download check so it can be repaired.</summary>
public class ModelStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"model-test-{Guid.NewGuid():N}");

    // Tiny ApproxBytes keeps the floor at the 1 MB minimum; a 1_800_000-byte
    // scratch file exercises the ApproxBytes*0.9 branch without real model sizes.
    private static readonly ModelStore.WhisperModel FakeModel =
        new("test-model", "Test model", "ggml-test.bin", 2_000_000);

    public ModelStoreTests()
    {
        Directory.CreateDirectory(_dir);
        ModelStore.ModelDirBackend = () => _dir;
    }

    public void Dispose()
    {
        ModelStore.ModelDirBackend = null!;
        Directory.Delete(_dir, recursive: true);
    }

    private void WriteFile(long length)
    {
        using var fs = File.OpenWrite(ModelStore.ModelPath(FakeModel));
        fs.SetLength(length);
    }

    [Fact]
    public void MissingFileIsNotDownloaded()
    {
        Assert.False(ModelStore.IsDownloaded(FakeModel));
    }

    [Fact]
    public void TinyFileIsNotDownloaded()
    {
        WriteFile(1_000);
        Assert.False(ModelStore.IsDownloaded(FakeModel)); // below the 1 MB minimum floor
    }

    [Fact]
    public void JustUnderApprxBytesFloorIsNotDownloaded()
    {
        WriteFile(1_800_000 - 1); // floor = max(1 MB, 0.9 x 2_000_000) = 1_800_000
        Assert.False(ModelStore.IsDownloaded(FakeModel));
    }

    [Fact]
    public void AtApprxBytesFloorIsDownloaded()
    {
        WriteFile(1_800_000);
        Assert.True(ModelStore.IsDownloaded(FakeModel));
    }

    [Fact]
    public void MoveCorruptAsideParksTheIncompleteFile()
    {
        WriteFile(1_000);
        ModelStore.MoveCorruptAside(FakeModel);

        Assert.False(File.Exists(ModelStore.ModelPath(FakeModel))); // download path is clear
        var parked = Directory.GetFiles(_dir, "ggml-test.bin.corrupt-*");
        var file = Assert.Single(parked);
        Assert.Equal(1_000, new FileInfo(file).Length); // kept for diagnosis, not silently deleted
    }

    [Fact]
    public void MoveCorruptAsideIsANoOpWhenFileIsMissing()
    {
        ModelStore.MoveCorruptAside(FakeModel);
        Assert.Empty(Directory.GetFiles(_dir));
    }
}
