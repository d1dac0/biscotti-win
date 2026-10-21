using System.Text.Json.Serialization;

namespace Biscotti.Core.Calendar;

/// <summary>A calendar event offered for an explicit user-started recording.</summary>
public sealed record CalendarMeeting(
    string EventId,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<string> Attendees,
    string? Description = null,
    string? Location = null,
    string CalendarId = "ics:0",
    string CalendarTitle = "Calendar",
    string? Url = null,
    bool IsAllDay = false,
    int? TotalAttendees = null)
{
    [JsonIgnore]
    public string AttendeesDisplay => Attendees.Count == 0 ? "No attendee names in this event" : string.Join(", ", Attendees);
    [JsonIgnore]
    public Uri? ConferenceUrl => ConferenceLink.Find(Url, Location, Description);
    [JsonIgnore]
    public string? Platform => ConferenceLink.Platform(ConferenceUrl);
    [JsonIgnore]
    public bool IsMeetingLike => !IsAllDay && (ConferenceUrl is not null || (TotalAttendees ?? Attendees.Count) >= 2);
    [JsonIgnore]
    public string WhenDisplay => $"{StartsAt.ToLocalTime():ddd, MMM d · h:mm tt} – {EndsAt.ToLocalTime():h:mm tt}";
    [JsonIgnore]
    public string RowDetail => $"{WhenDisplay} · {Platform ?? CalendarTitle}";
}
