using Biscotti.Core;
using Biscotti.Core.Models;
using Biscotti.Mcp;

namespace Biscotti.Tests;

public sealed class MeetingHistorySearchTests
{
    [Theory]
    [InlineData("title", "Title:")]
    [InlineData("summary", "Summary:")]
    [InlineData("private note", "Notes:")]
    [InlineData("timed note", "Note:")]
    [InlineData("action", "Action:")]
    [InlineData("transcript", "Transcript:")]
    [InlineData("speaker", "Speaker:")]
    public void UiAndMcpMatchEveryExistingField(string query, string label)
    {
        var meeting = new Meeting
        {
            Title = "Title", Summary = "Summary", Notes = "A private note",
            ActionItems = ["Action"],
            TimestampedNotes = [new() { Text = "Timed note", StartSeconds = 1 }],
            Transcript = [new() { Speaker = "Speaker", Text = "Transcript", StartSeconds = 2 }],
        };
        var normalized = $"  {query.ToUpperInvariant()}  ";
        var result = Assert.Single(MeetingHistorySearch.Search([MeetingSearchDocument.FromMeeting(meeting)], normalized));
        Assert.StartsWith(label, result.Snippet);
        Assert.Single(new MeetingQueryService(() => [meeting]).SearchMeetings(normalized).Meetings);
    }

    [Fact]
    public void LiteralPhraseRankingAndNewestTieBreakAreDeterministic()
    {
        var transcript = new Meeting { Title = "Other", StartedUtc = new DateTime(2026, 10, 20), Transcript = [new() { Speaker = "A", Text = "Atlas plans", StartSeconds = 1 }] };
        var oldTitle = new Meeting { Title = "Atlas plans", StartedUtc = new DateTime(2026, 10, 18) };
        var newTitle = new Meeting { Title = "Atlas plans", StartedUtc = new DateTime(2026, 10, 19) };
        var docs = new[] { transcript, oldTitle, newTitle }.Select(MeetingSearchDocument.FromMeeting).ToArray();
        Assert.Equal(new[] { newTitle.Id, oldTitle.Id, transcript.Id }, MeetingHistorySearch.Search(docs, "Atlas plans").Select(r => r.Meeting.Id));
        Assert.Empty(MeetingHistorySearch.Search(docs, "plans Atlas"));
        Assert.Equal(new[] { transcript.Id, newTitle.Id, oldTitle.Id }, MeetingHistorySearch.Search(docs, "  ").Select(r => r.Meeting.Id));
        Assert.Empty(MeetingHistorySearch.Search([], "Atlas"));
    }

    [Fact]
    public void ExcerptShowsMatchInLongTextAndSnapshotDoesNotChangeWhileFiltering()
    {
        var meeting = new Meeting { Notes = new string('a', 500) + " needle " + new string('z', 500) };
        var doc = MeetingSearchDocument.FromMeeting(meeting);
        meeting.Notes = "edited";
        var result = Assert.Single(MeetingHistorySearch.Search([doc], "needle"));
        Assert.Contains("needle", result.Snippet);
        Assert.StartsWith("Notes: …", result.Snippet);
        Assert.EndsWith("…", result.Snippet);
        Assert.True(result.Snippet.Length < 200);
        Assert.Empty(MeetingHistorySearch.Search([MeetingSearchDocument.FromMeeting(meeting)], "needle"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => MeetingHistorySearch.Search([doc], "needle", cancellation.Token));
    }
}
