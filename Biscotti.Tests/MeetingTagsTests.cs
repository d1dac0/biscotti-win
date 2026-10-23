using System.Text;
using Biscotti.Core;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;
using Xunit;

namespace Biscotti.Tests;

[Collection("MeetingStore")]
public sealed class MeetingTagsTests
{
    [Fact]
    public void Normalize_CollapsesWhitespaceClampsLengthAndRejectsBlank()
    {
        Assert.Equal("Client Acme", MeetingTags.Normalize("  Client   Acme "));
        Assert.Null(MeetingTags.Normalize("   "));
        Assert.Null(MeetingTags.Normalize(null));
        Assert.Equal(new string('a', MeetingTags.MaxLength), MeetingTags.Normalize(new string('a', MeetingTags.MaxLength + 10)));
    }

    [Fact]
    public void TryAdd_DedupesCaseInsensitivelyAndCapsCount()
    {
        var meeting = new Meeting();
        Assert.True(MeetingTags.TryAdd(meeting, "Atlas"));
        Assert.False(MeetingTags.TryAdd(meeting, " ATLAS "));
        Assert.False(MeetingTags.TryAdd(meeting, "  "));
        for (var i = meeting.Tags.Count; i < MeetingTags.MaxPerMeeting; i++)
            Assert.True(MeetingTags.TryAdd(meeting, $"tag {i}"));
        Assert.False(MeetingTags.TryAdd(meeting, "one too many"));
        Assert.Equal(MeetingTags.MaxPerMeeting, meeting.Tags.Count);
    }

    [Fact]
    public void Remove_IsCaseInsensitive()
    {
        var meeting = new Meeting();
        MeetingTags.TryAdd(meeting, "Atlas");
        Assert.True(MeetingTags.Remove(meeting, "atlas"));
        Assert.Empty(meeting.Tags);
        Assert.False(MeetingTags.Remove(meeting, "atlas"));
    }

    [Fact]
    public void Summarize_CountsMeetingsAndKeepsFirstSpelling()
    {
        var first = new Meeting();
        MeetingTags.TryAdd(first, "Atlas");
        MeetingTags.TryAdd(first, "Planning");
        var second = new Meeting();
        MeetingTags.TryAdd(second, "ATLAS");
        var summary = MeetingTags.Summarize(new[] { first, second });
        Assert.Equal(2, summary.Count);
        var atlas = summary.Single(s => s.Tag.Equals("atlas", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Atlas", atlas.Tag); // first-seen spelling wins
        Assert.Equal(2, atlas.Count);
        Assert.Equal(1, summary.Single(s => s.Tag == "Planning").Count);
    }

    [Fact]
    public void RenameAll_RenamesAcrossMeetingsCaseInsensitivelyAndMerges()
    {
        var a = new Meeting();
        MeetingTags.TryAdd(a, "Atlas");
        var b = new Meeting();
        MeetingTags.TryAdd(b, "ATLAS");
        MeetingTags.TryAdd(b, "Client work");
        var c = new Meeting(); // untouched
        MeetingTags.TryAdd(c, "Planning");

        var affected = MeetingTags.RenameAll(new[] { a, b, c }, "atlas", "Atlas project");
        Assert.Equal(new[] { a, b }, affected);
        Assert.Equal(new[] { "Atlas project" }, a.Tags);
        Assert.Equal(new[] { "Atlas project", "Client work" }, b.Tags); // renamed in place, order retained
        Assert.Equal(new[] { "Planning" }, c.Tags);

        // Renaming onto an existing tag merges into it.
        var merged = MeetingTags.RenameAll(new[] { a, b }, "Client work", "Atlas project");
        Assert.Equal(new[] { b }, merged);
        Assert.Single(b.Tags);
        Assert.Equal("Atlas project", b.Tags[0]);

        // A blank or overlong target renames nothing.
        Assert.Empty(MeetingTags.RenameAll(new[] { a }, "Atlas project", "   "));
    }

    [Fact]
    public void RemoveAll_RemovesTagFromEveryMeeting()
    {
        var a = new Meeting();
        MeetingTags.TryAdd(a, "Atlas");
        var b = new Meeting();
        MeetingTags.TryAdd(b, "Atlas");
        MeetingTags.TryAdd(b, "Planning");
        var affected = MeetingTags.RemoveAll(new[] { a, b }, "ATLAS");
        Assert.Equal(new[] { a, b }, affected);
        Assert.Empty(a.Tags);
        Assert.Equal(new[] { "Planning" }, b.Tags);
    }

    [Fact]
    public void SavedTags_RoundTripThroughProtectedStorageAndMarkdown()
    {
        MeetingStore.RootOverride = Path.Combine(Path.GetTempPath(), $"biscotti-tags-{Guid.NewGuid()}");
        try
        {
            var meeting = new Meeting { Title = "Tagged standup" };
            MeetingTags.TryAdd(meeting, "Atlas");
            MeetingTags.TryAdd(meeting, "Client work");
            MeetingStore.Save(meeting);

            var jsonPath = Path.Combine(MeetingStore.MeetingDir(meeting.Id), "meeting.json");
            var jsonFile = File.ReadAllBytes(jsonPath);
            Assert.True(jsonFile.AsSpan().StartsWith("BISCOTTI-DPAPI:1\n"u8));
            Assert.DoesNotContain("Atlas", Encoding.UTF8.GetString(jsonFile), StringComparison.Ordinal);

            var loaded = MeetingStore.LoadAll().Single(m => m.Id == meeting.Id);
            Assert.Equal(new[] { "Atlas", "Client work" }, loaded.Tags);

            var markdownBytes = ProtectedMeetingText.ReadAndMigrate(Path.Combine(MeetingStore.MeetingDir(meeting.Id), "meeting.md"));
            var markdown = Encoding.UTF8.GetString(markdownBytes);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(markdownBytes);
            Assert.Contains("**Tags:** Atlas, Client work", markdown, StringComparison.Ordinal);
        }
        finally
        {
            var dir = MeetingStore.RootOverride!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            MeetingStore.RootOverride = null;
        }
    }

    [Fact]
    public void HistorySearch_FindsMeetingsByTagText()
    {
        var meeting = new Meeting { Title = "Standup" };
        MeetingTags.TryAdd(meeting, "Atlas");
        var results = MeetingHistorySearch.Search(new[] { MeetingSearchDocument.FromMeeting(meeting) }, "atlas");
        var result = Assert.Single(results);
        Assert.Same(meeting, result.Meeting);
        Assert.StartsWith("Tag:", result.Snippet, StringComparison.Ordinal);
        Assert.Equal("Tags: Atlas", result.TagsLine);
    }
}
