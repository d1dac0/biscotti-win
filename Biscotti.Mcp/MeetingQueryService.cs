using Biscotti.Core.Models;
using Biscotti.Core.Storage;

namespace Biscotti.Mcp;

public sealed class MeetingQueryService
{
    private const int DefaultSearchLimit = 20;
    private const int MaximumSearchLimit = 100;
    private const int DefaultTranscriptLimit = 200;
    private const int MaximumTranscriptLimit = 500;
    private readonly Func<List<Meeting>> _loadMeetings;

    public MeetingQueryService() : this(MeetingStore.LoadMetadataOnly) { }

    public MeetingQueryService(Func<List<Meeting>> loadMeetings) =>
        _loadMeetings = loadMeetings ?? throw new ArgumentNullException(nameof(loadMeetings));

    public SearchMeetingsResponse SearchMeetings(string query, int limit = DefaultSearchLimit)
    {
        var normalizedQuery = query.Trim();
        var boundedLimit = Math.Clamp(limit, 1, MaximumSearchLimit);
        var matches = _loadMeetings()
            .Where(meeting => Matches(meeting, normalizedQuery))
            .ToList();

        return new SearchMeetingsResponse(
            TotalMatches: matches.Count,
            Returned: Math.Min(matches.Count, boundedLimit),
            Limit: boundedLimit,
            Meetings: matches.Take(boundedLimit).Select(ToSearchResult).ToList());
    }

    public MeetingDetailsResponse GetMeeting(string meetingId)
    {
        if (!Guid.TryParse(meetingId, out var id))
            return new MeetingDetailsResponse(false, null, "Meeting id must be a GUID.");

        var meeting = _loadMeetings().FirstOrDefault(candidate => candidate.Id == id);
        return meeting is null
            ? new MeetingDetailsResponse(false, null, "Meeting was not found.")
            : new MeetingDetailsResponse(true, ToDetails(meeting), null);
    }

    public TranscriptResponse GetTranscript(string meetingId, int offset = 0, int limit = DefaultTranscriptLimit)
    {
        if (!Guid.TryParse(meetingId, out var id))
            return new TranscriptResponse(false, null, 0, 0, 0, false, "Meeting id must be a GUID.");

        var meeting = _loadMeetings().FirstOrDefault(candidate => candidate.Id == id);
        if (meeting is null)
            return new TranscriptResponse(false, id, 0, 0, 0, false, "Meeting was not found.");

        var boundedOffset = Math.Max(0, offset);
        var boundedLimit = Math.Clamp(limit, 1, MaximumTranscriptLimit);
        var lines = meeting.Transcript.OrderBy(line => line.StartSeconds).ToList();
        var page = lines.Skip(boundedOffset).Take(boundedLimit)
            .Select(line => new TranscriptSegment(line.Speaker, line.Text, line.StartSeconds, line.EndSeconds))
            .ToList();
        var nextOffset = boundedOffset + page.Count;

        return new TranscriptResponse(
            Found: true,
            MeetingId: meeting.Id,
            TotalLines: lines.Count,
            Offset: boundedOffset,
            Limit: boundedLimit,
            HasMore: nextOffset < lines.Count,
            Message: null,
            Lines: page);
    }

    private static bool Matches(Meeting meeting, string query)
    {
        if (query.Length == 0) return true;
        if (Contains(meeting.Title, query) || Contains(meeting.Summary, query) || Contains(meeting.Notes, query)) return true;
        if (meeting.ActionItems.Any(item => Contains(item, query))) return true;
        if (meeting.TimestampedNotes.Any(note => Contains(note.Text, query))) return true;
        return meeting.Transcript.Any(line => Contains(line.Text, query) || Contains(line.Speaker, query));
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static MeetingSearchResult ToSearchResult(Meeting meeting) =>
        new(meeting.Id, meeting.Title, meeting.StartedUtc, meeting.EndedUtc,
            meeting.Summary, meeting.Transcript.Count, meeting.TimestampedNotes.Count);

    private static MeetingDetails ToDetails(Meeting meeting) =>
        new(meeting.Id, meeting.Title, meeting.StartedUtc, meeting.EndedUtc, meeting.Summary,
            meeting.Notes, meeting.ActionItems.ToList(), meeting.CalendarAttendees.ToList(),
            meeting.TimestampedNotes.OrderBy(note => note.StartSeconds)
                .Select(note => new TimestampedNoteDetails(note.Text, note.StartSeconds)).ToList(),
            meeting.Transcript.Count);
}

public sealed record SearchMeetingsResponse(
    int TotalMatches,
    int Returned,
    int Limit,
    IReadOnlyList<MeetingSearchResult> Meetings);

public sealed record MeetingSearchResult(
    Guid Id,
    string Title,
    DateTime StartedUtc,
    DateTime? EndedUtc,
    string? Summary,
    int TranscriptLineCount,
    int TimestampedNoteCount);

public sealed record MeetingDetailsResponse(bool Found, MeetingDetails? Meeting, string? Message);

public sealed record MeetingDetails(
    Guid Id,
    string Title,
    DateTime StartedUtc,
    DateTime? EndedUtc,
    string? Summary,
    string? Notes,
    IReadOnlyList<string> ActionItems,
    IReadOnlyList<string> CalendarAttendees,
    IReadOnlyList<TimestampedNoteDetails> TimestampedNotes,
    int TranscriptLineCount);

public sealed record TimestampedNoteDetails(string Text, double StartSeconds);

public sealed record TranscriptResponse(
    bool Found,
    Guid? MeetingId,
    int TotalLines,
    int Offset,
    int Limit,
    bool HasMore,
    string? Message,
    IReadOnlyList<TranscriptSegment>? Lines = null);

public sealed record TranscriptSegment(string Speaker, string Text, double StartSeconds, double EndSeconds);
