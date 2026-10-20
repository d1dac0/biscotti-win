using Biscotti.Core.Models;

namespace Biscotti.Core;

/// <summary>Immutable text snapshots, held in memory only. Never write a search index.</summary>
public sealed record MeetingSearchDocument(Meeting Meeting, IReadOnlyList<MeetingSearchField> Fields)
{
    public static MeetingSearchDocument FromMeeting(Meeting meeting)
    {
        var fields = new List<MeetingSearchField>();
        void Add(string label, string? text, int rank)
        {
            if (!string.IsNullOrWhiteSpace(text)) fields.Add(new(label, text, rank));
        }
        Add("Title", meeting.Title, 0);
        Add("Summary", meeting.Summary, 1);
        Add("Notes", meeting.Notes, 2);
        foreach (var note in meeting.TimestampedNotes) Add("Note", note.Text, 2);
        foreach (var action in meeting.ActionItems) Add("Action", action, 3);
        foreach (var line in meeting.Transcript)
        {
            Add("Transcript", line.Text, 4);
            Add("Speaker", line.Speaker, 4);
        }
        return new(meeting, fields.AsReadOnly());
    }
}

public sealed record MeetingSearchField(string Label, string Text, int Rank);
public sealed record MeetingHistoryResult(Meeting Meeting, string Snippet, int Rank)
{
    public string DateGroup => Meeting.StartedUtc.ToLocalTime().ToString("yyyy-MM");
}

public static class MeetingHistorySearch
{
    public static bool Matches(Meeting meeting, string query) =>
        Match(MeetingSearchDocument.FromMeeting(meeting), query.Trim()) is not null;

    public static List<MeetingHistoryResult> Search(IEnumerable<MeetingSearchDocument> documents, string query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Trim();
        var results = new List<MeetingHistoryResult>();
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Match(document, normalized) is { } result) results.Add(result);
        }
        return results.OrderBy(r => r.Rank).ThenByDescending(r => r.Meeting.StartedUtc).ToList();
    }

    private static MeetingHistoryResult? Match(MeetingSearchDocument document, string query)
    {
        if (query.Length == 0) return new(document.Meeting, "", 0);
        foreach (var field in document.Fields)
        {
            var index = field.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            // Include context around the match, rather than always truncating the start.
            var start = Math.Max(0, index - 45);
            var length = Math.Min(180, field.Text.Length - start);
            var excerpt = field.Text.Substring(start, length).Replace('\r', ' ').Replace('\n', ' ');
            var snippet = $"{field.Label}: {(start > 0 ? "…" : "")}{excerpt}{(start + length < field.Text.Length ? "…" : "")}";
            return new(document.Meeting, snippet, field.Rank);
        }
        return null;
    }
}
