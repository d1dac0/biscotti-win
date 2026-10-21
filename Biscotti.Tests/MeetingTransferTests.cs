using System.Globalization;
using System.Text;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;

namespace Biscotti.Tests;

[CollectionDefinition("MeetingStore", DisableParallelization = true)]
public sealed class MeetingStoreCollection;

[Collection("MeetingStore")]
public sealed class MeetingTransferTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Biscotti-transfer-" + Guid.NewGuid());

    public MeetingTransferTests()
    {
        Directory.CreateDirectory(_directory);
        MeetingStore.RootOverride = Path.Combine(_directory, "Meetings");
    }

    public void Dispose()
    {
        MeetingStore.RootOverride = null;
        Directory.Delete(_directory, recursive: true);
    }

    private static Meeting Synthetic() => new()
    {
        Title = "Atlas, \"planning\" — café\r\nSecond line",
        StartedUtc = new DateTime(2026, 10, 20, 15, 12, 34, DateTimeKind.Utc).AddTicks(1234567),
        EndedUtc = new DateTime(2026, 10, 20, 16, 0, 0, DateTimeKind.Utc),
        Notes = "=notes are literal\n\"quoted\", text", Summary = "## Decisions\n\nShip it.",
        ActionItems = ["Alex, send \"notes\""], CalendarAttendees = ["Alex", "Jordan"],
        Transcript = [new() { Speaker = "Alex", Text = "Hello, \"world\"\nNext line", StartSeconds = 12.25, EndSeconds = 13.75 }],
        TimestampedNotes = [new() { Text = "Remember, this", StartSeconds = 12.5 }],
    };

    [Fact]
    public void CsvRoundTripPreservesUnicodeQuotesNewlinesAndAllTimesAcrossCultures()
    {
        var original = Synthetic();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var csv = MeetingTransfer.RenderCsv([original]);
            var preview = MeetingTransfer.PreviewCsv("\uFEFF" + csv, []);
            var copy = Assert.Single(preview.Meetings);
            Assert.Empty(preview.Errors);
            Assert.Equal(0, preview.DuplicateRows);
            Assert.Equal(csv, MeetingTransfer.RenderCsv([copy]));
            Assert.Equal(DateTimeKind.Utc, copy.StartedUtc.Kind);
            Assert.Equal(original.StartedUtc, copy.StartedUtc);
            Assert.Equal(original.EndedUtc, copy.EndedUtc);
            Assert.Equal(12.25, copy.Transcript[0].StartSeconds);
            Assert.Equal(13.75, copy.Transcript[0].EndSeconds);
            Assert.Equal(12.5, copy.TimestampedNotes[0].StartSeconds);
            Assert.Equal(original.Title, copy.Title);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void PreviewCountsInvalidAndDuplicateRowsWithoutWritingAnything()
    {
        var existing = Synthetic();
        var valid = Synthetic();
        var invalid = Synthetic();
        invalid.Transcript[0].StartSeconds = -1;
        var preview = MeetingTransfer.PreviewCsv(MeetingTransfer.RenderCsv([existing, valid, valid, invalid]), [existing.Id]);
        Assert.Single(preview.Meetings);
        Assert.Equal(2, preview.DuplicateRows);
        Assert.Single(preview.Errors);
        Assert.Contains("Row 5", preview.Errors[0]);
        Assert.False(Directory.Exists(MeetingStore.Root));
    }

    [Fact]
    public void ImportProtectsBothFilesAndRepeatOrConcurrentImportNeverOverwrites()
    {
        var original = Synthetic();
        var preview = MeetingTransfer.PreviewCsv(MeetingTransfer.RenderCsv([original]), []);
        Assert.Equal(new ImportResult(1, 0, false, null), MeetingTransfer.Import(preview));
        var json = File.ReadAllBytes(Path.Combine(MeetingStore.MeetingDir(original.Id), "meeting.json"));
        var markdown = File.ReadAllBytes(Path.Combine(MeetingStore.MeetingDir(original.Id), "meeting.md"));
        Assert.True(json.AsSpan().StartsWith("BISCOTTI-DPAPI:1\n"u8));
        Assert.True(markdown.AsSpan().StartsWith("BISCOTTI-DPAPI:1\n"u8));
        Assert.Equal(original.Title, Assert.Single(MeetingStore.LoadMetadataOnly()).Title);
        preview.Meetings[0].Title = "Must not replace existing data";
        Assert.Equal(new ImportResult(0, 1, false, null), MeetingTransfer.Import(preview));
        Assert.Equal(json, File.ReadAllBytes(Path.Combine(MeetingStore.MeetingDir(original.Id), "meeting.json")));
        Assert.Equal(markdown, File.ReadAllBytes(Path.Combine(MeetingStore.MeetingDir(original.Id), "meeting.md")));
        Assert.Empty(Directory.GetDirectories(MeetingStore.Root, ".import-*"));
    }

    [Fact]
    public void CancelledPreviewImportAndExportLeaveStorageAndDestinationUntouched()
    {
        var csv = MeetingTransfer.RenderCsv([Synthetic()]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => MeetingTransfer.PreviewCsv(csv, [], cancellation.Token));
        var preview = MeetingTransfer.PreviewCsv(csv, []);
        Assert.Equal(new ImportResult(0, 0, true, null), MeetingTransfer.Import(preview, cancellation.Token));
        Assert.False(Directory.Exists(MeetingStore.Root));
        var destination = Path.Combine(_directory, "existing.csv");
        File.WriteAllText(destination, "keep me");
        Assert.Throws<OperationCanceledException>(() => MeetingTransfer.ExportText(destination, csv, cancellation.Token));
        Assert.Equal("keep me", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void ExportWritesOnlyChosenFileAndRefusesProtectedStorage()
    {
        var meeting = Synthetic();
        MeetingStore.Save(meeting);
        var protectedPath = Path.Combine(MeetingStore.MeetingDir(meeting.Id), "meeting.md");
        var original = File.ReadAllBytes(protectedPath);
        Assert.Throws<InvalidOperationException>(() => MeetingTransfer.ExportText(protectedPath, "plaintext"));
        Assert.Equal(original, File.ReadAllBytes(protectedPath));
        var destination = Path.Combine(_directory, "export.csv");
        MeetingTransfer.ExportText(destination, MeetingTransfer.RenderCsv([meeting]));
        Assert.Equal(meeting.Title, Assert.Single(MeetingTransfer.PreviewFile(destination, []).Meetings).Title);
        MeetingTransfer.ExportText(destination, MeetingStore.RenderMarkdown(meeting));
        Assert.Contains("[00:00:12]", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void CancellationAfterFirstCommitKeepsCompletedMeetingAndRetrySkipsIt()
    {
        var first = Synthetic();
        var second = Synthetic();
        var preview = MeetingTransfer.PreviewCsv(MeetingTransfer.RenderCsv([first, second]), []);
        using var cancellation = new CancellationTokenSource();
        var result = MeetingTransfer.Import(preview, cancellation.Token, new CancelAfterCommit(cancellation));
        Assert.Equal(new ImportResult(1, 0, true, null), result);
        Assert.Equal(first.Id, Assert.Single(MeetingStore.LoadMetadataOnly()).Id);
        Assert.Equal(new ImportResult(1, 1, false, null), MeetingTransfer.Import(preview));
        Assert.Equal(2, MeetingStore.LoadMetadataOnly().Count);
    }

    private sealed class CancelAfterCommit(CancellationTokenSource cancellation) : IProgress<int>
    {
        public void Report(int value) => cancellation.Cancel();
    }

    [Fact]
    public void OffsetDatesNormalizeToUtcAndMissingOffsetsAreInvalid()
    {
        var csv = MeetingTransfer.RenderCsv([Synthetic()]);
        var offset = csv.Replace("2026-10-20T15:12:34.1234567Z", "2026-10-20T10:12:34.1234567-05:00");
        Assert.Equal(Synthetic().StartedUtc, Assert.Single(MeetingTransfer.PreviewCsv(offset, []).Meetings).StartedUtc);
        var missing = csv.Replace("2026-10-20T15:12:34.1234567Z", "2026-10-20T15:12:34.1234567");
        Assert.Single(MeetingTransfer.PreviewCsv(missing, []).Errors);
    }

    [Fact]
    public void MalformedHeaderQuotesAndOversizedInputFailBeforeImport()
    {
        Assert.Throws<InvalidDataException>(() => MeetingTransfer.PreviewCsv("Title,Notes\r\nhello,note", []));
        var csv = MeetingTransfer.RenderCsv([Synthetic()]);
        Assert.Throws<InvalidDataException>(() => MeetingTransfer.PreviewCsv(csv + "\"unclosed", []));
        Assert.Throws<InvalidDataException>(() => MeetingTransfer.PreviewCsv(new string('a', MeetingTransfer.MaxCsvBytes + 1), []));
        var path = Path.Combine(_directory, "large.csv");
        using (var file = File.Create(path)) file.SetLength(MeetingTransfer.MaxCsvBytes + 1);
        Assert.Throws<InvalidDataException>(() => MeetingTransfer.PreviewFile(path, []));
        Assert.False(Directory.Exists(MeetingStore.Root));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"Speaker\":\"Alex\",\"StartSeconds\":0}]")]
    public void InvalidTranscriptCollectionsAreReported(string transcript)
    {
        var m = Synthetic();
        var csv = MeetingTransfer.RenderCsv([m]);
        var serialized = System.Text.Json.JsonSerializer.Serialize(m.Transcript).Replace("\"", "\"\"");
        csv = csv.Replace(serialized, transcript.Replace("\"", "\"\""));
        var preview = MeetingTransfer.PreviewCsv(csv, []);
        Assert.Empty(preview.Meetings);
        Assert.Single(preview.Errors);
    }

    [Fact]
    public void InterruptedStagingIsNotLoadedAndStorageFailureIsReported()
    {
        var staging = Path.Combine(MeetingStore.Root, ".import-interrupted");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "meeting.json"), "incomplete");
        Assert.Empty(MeetingStore.LoadMetadataOnly());
        MeetingStore.RootOverride = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(MeetingStore.Root, "blocked");
        var preview = MeetingTransfer.PreviewCsv(MeetingTransfer.RenderCsv([Synthetic()]), []);
        var result = MeetingTransfer.Import(preview);
        Assert.Equal(0, result.Imported);
        Assert.NotNull(result.Error);
        Assert.False(result.Cancelled);
    }
}
