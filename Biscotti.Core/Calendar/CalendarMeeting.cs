namespace Biscotti.Core.Calendar;

/// <summary>A calendar event offered for an explicit user-started recording.</summary>
public sealed record CalendarMeeting(
    string EventId,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<string> Attendees)
{
    public string AttendeesDisplay => Attendees.Count == 0 ? "No attendee names in this event" : string.Join(", ", Attendees);
}
