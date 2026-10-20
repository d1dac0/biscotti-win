using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Biscotti.Mcp;

[McpServerToolType]
public sealed class MeetingTools(MeetingQueryService meetings)
{
    [McpServerTool(Name = "search-meetings", Title = "Search local meetings", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Search locally stored meetings by title, summary, notes, action items, transcript text, or speaker. An empty query lists recent meetings.")]
    public SearchMeetingsResponse SearchMeetings(
        [Description("Text to search for. An empty string returns recent meetings.")] string query,
        [Description("Maximum number of meetings to return (1 to 100). Defaults to 20.")] int limit = 20) =>
        meetings.SearchMeetings(query, limit);

    [McpServerTool(Name = "get-meeting", Title = "Get local meeting details", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get a locally stored meeting's title, dates, summary, notes, action items, attendees, and transcript count by its GUID.")]
    public MeetingDetailsResponse GetMeeting(
        [Description("The meeting GUID returned by search-meetings.")] string meetingId) =>
        meetings.GetMeeting(meetingId);

    [McpServerTool(Name = "get-transcript", Title = "Get a meeting transcript page", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get transcript text with speaker labels and recording timestamps. Use offset and limit to page through long transcripts.")]
    public TranscriptResponse GetTranscript(
        [Description("The meeting GUID returned by search-meetings.")] string meetingId,
        [Description("Zero-based transcript line offset. Defaults to 0.")] int offset = 0,
        [Description("Maximum number of lines to return (1 to 500). Defaults to 200.")] int limit = 200) =>
        meetings.GetTranscript(meetingId, offset, limit);
}
