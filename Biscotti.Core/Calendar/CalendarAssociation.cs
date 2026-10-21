using Biscotti.Core.Models;

namespace Biscotti.Core.Calendar;

public static class CalendarAssociation
{
    /// <summary>A failed protected save must not change the shared meeting or its invitee roster.</summary>
    public static void Set(Meeting meeting, CalendarMeeting? calendarEvent, Action<Meeting> save)
    {
        var previous = meeting.CalendarEvent;
        var previousAttendees = meeting.CalendarAttendees;
        meeting.CalendarEvent = calendarEvent;
        meeting.CalendarAttendees = calendarEvent?.Attendees.ToList() ?? [];
        try { save(meeting); }
        catch
        {
            meeting.CalendarEvent = previous;
            meeting.CalendarAttendees = previousAttendees;
            throw;
        }
    }
}
