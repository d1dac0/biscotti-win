using Biscotti.Core.Transcription;
using Xunit;

namespace Biscotti.Tests;

/// <summary>Issue #32: a present-but-truncated model file must fail the download check so it can be repaired.</summary>
public class ModelStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"model-test-{Guid.NewGuid():N}");
    private readonly Func<string> _previousDirectory = ModelStore.ModelDirBackend;

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
        ModelStore.ModelDirBackend = _previousDirectory;
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

    private sealed class DownloadHandler(Func<HttpRequestMessage, CancellationToken, Task<System.Net.Http.HttpResponseMessage>> send)
        : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }

    [Fact]
    public async Task NoDiskDoesNotStartDownloadOrMoveExistingFile()
    {
        WriteFile(1000);
        using var http = new System.Net.Http.HttpClient(new DownloadHandler((_, _) => throw new Exception("Must not call network")));
        await Assert.ThrowsAsync<IOException>(() => ModelStore.DownloadAsync(FakeModel, http, () => 0, null));
        Assert.Equal(1000, new FileInfo(ModelStore.ModelPath(FakeModel)).Length);
    }

    [Fact]
    public async Task TruncatedResponseIsRejectedAndRetryStartsFresh()
    {
        var calls = 0;
        using var http = new System.Net.Http.HttpClient(new DownloadHandler((_, _) => Task.FromResult(
            new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new System.Net.Http.ByteArrayContent(new byte[++calls == 1 ? 500 : 2_000_000]) })));
        await Assert.ThrowsAsync<InvalidDataException>(() => ModelStore.DownloadAsync(FakeModel, http, () => long.MaxValue, null));
        Assert.False(ModelStore.IsDownloaded(FakeModel));
        await ModelStore.DownloadAsync(FakeModel, http, () => long.MaxValue, null);
        Assert.True(ModelStore.IsDownloaded(FakeModel));
    }

    [Fact]
    public async Task IgnoredRangeReplacesPartialFileInsteadOfAppending()
    {
        File.WriteAllBytes(ModelStore.ModelPath(FakeModel) + ".part", new byte[100]);
        using var http = new System.Net.Http.HttpClient(new DownloadHandler((request, _) =>
        {
            Assert.Equal(100, request.Headers.Range!.Ranges.Single().From);
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new System.Net.Http.ByteArrayContent(new byte[2_000_000]) });
        }));
        await ModelStore.DownloadAsync(FakeModel, http, () => long.MaxValue, null);
        Assert.Equal(2_000_000, new FileInfo(ModelStore.ModelPath(FakeModel)).Length);
    }

    [Fact]
    public async Task ValidRangeResumesAndPublishesOnlyCompletedModel()
    {
        File.WriteAllBytes(ModelStore.ModelPath(FakeModel) + ".part", new byte[100]);
        using var http = new System.Net.Http.HttpClient(new DownloadHandler((_, _) =>
        {
            var content = new System.Net.Http.ByteArrayContent(new byte[1_999_900]);
            content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(100, 1_999_999, 2_000_000);
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.PartialContent) { Content = content });
        }));
        await ModelStore.DownloadAsync(FakeModel, http, () => long.MaxValue, null);
        Assert.Equal(2_000_000, new FileInfo(ModelStore.ModelPath(FakeModel)).Length);
        Assert.False(File.Exists(ModelStore.ModelPath(FakeModel) + ".part"));
    }

    [Fact]
    public async Task RejectedRangeClearsPartialSoRetryCanRecover()
    {
        var partial = ModelStore.ModelPath(FakeModel) + ".part";
        File.WriteAllBytes(partial, new byte[100]);
        using var http = new System.Net.Http.HttpClient(new DownloadHandler((_, _) => Task.FromResult(
            new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.RequestedRangeNotSatisfiable))));
        await Assert.ThrowsAsync<InvalidDataException>(() => ModelStore.DownloadAsync(FakeModel, http, () => long.MaxValue, null));
        Assert.False(File.Exists(partial));
        Assert.False(ModelStore.IsDownloaded(FakeModel));
    }

    [Fact]
    public async Task CancellationLeavesPartialFileForRetryAndDoesNotPublishModel()
    {
        using var cts = new CancellationTokenSource();
        using var http = new System.Net.Http.HttpClient(new DownloadHandler((_, _) =>
        {
            cts.Cancel();
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new System.Net.Http.ByteArrayContent(new byte[2_000_000]) });
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ModelStore.DownloadAsync(FakeModel, http, () => long.MaxValue, null, cts.Token));
        Assert.False(ModelStore.IsDownloaded(FakeModel));
    }
}
