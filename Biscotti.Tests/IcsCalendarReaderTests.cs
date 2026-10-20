using Biscotti.Core.Calendar;

namespace Biscotti.Tests;

public sealed class IcsCalendarReaderTests
{
    [Fact]
    public void ReadUpcoming_ParsesUtcEventsAndPrivateAttendeeNames()
    {
        var now = new DateTimeOffset(2026, 10, 20, 15, 30, 0, TimeSpan.Zero);
        var meetings = ReadCalendar(now, """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:design-review
            DTSTART:20261020T160000Z
            DTEND:20261020T170000Z
            SUMMARY:Design review
            ATTENDEE;CN=Alex Example:mailto:alex@example.test
            ATTENDEE;CN=Sam Example:mailto:sam@example.test
            END:VEVENT
            END:VCALENDAR
            """);

        var meeting = Assert.Single(meetings);
        Assert.Equal("Design review", meeting.Title);
        Assert.Equal(now.AddMinutes(30), meeting.StartsAt);
        Assert.Equal(now.AddMinutes(90), meeting.EndsAt);
        Assert.Equal(new[] { "Alex Example", "Sam Example" }, meeting.Attendees);
    }

    [Fact]
    public void ReadUpcoming_ExpandsRecurringEventsAndOmitsCancelledEvents()
    {
        var now = new DateTimeOffset(2026, 10, 20, 15, 30, 0, TimeSpan.Zero);
        var meetings = ReadCalendar(now, """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:daily-sync
            DTSTART:20261020T160000Z
            DTEND:20261020T163000Z
            RRULE:FREQ=DAILY;COUNT=3
            SUMMARY:Daily sync
            END:VEVENT
            BEGIN:VEVENT
            UID:cancelled
            DTSTART:20261020T170000Z
            DTEND:20261020T173000Z
            STATUS:CANCELLED
            SUMMARY:Cancelled event
            END:VEVENT
            END:VCALENDAR
            """, window: TimeSpan.FromDays(3));

        Assert.Equal(3, meetings.Count);
        Assert.All(meetings, meeting => Assert.Equal("Daily sync", meeting.Title));
        Assert.Equal(now.AddDays(1).AddMinutes(30), meetings[1].StartsAt);
    }

    [Fact]
    public void ReadUpcoming_PreservesEventTimeZoneAcrossDaylightSavingChange()
    {
        var now = new DateTimeOffset(2026, 10, 31, 14, 0, 0, TimeSpan.Zero);
        var meetings = ReadCalendar(now, """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:new-york-daily
            DTSTART;TZID=America/New_York:20261031T110000
            DTEND;TZID=America/New_York:20261031T113000
            RRULE:FREQ=DAILY;COUNT=3
            SUMMARY:Daily New York meeting
            END:VEVENT
            END:VCALENDAR
            """, window: TimeSpan.FromDays(3));

        Assert.Equal(3, meetings.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 15, 0, 0, TimeSpan.Zero), meetings[0].StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 16, 0, 0, TimeSpan.Zero), meetings[1].StartsAt);
    }

    [Fact]
    public void ReadUpcoming_EnforcesWindowAndEventLimit()
    {
        var now = new DateTimeOffset(2026, 10, 20, 15, 30, 0, TimeSpan.Zero);
        var meetings = ReadCalendar(now, """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:first
            DTSTART:20261020T160000Z
            DTEND:20261020T163000Z
            SUMMARY:First
            END:VEVENT
            BEGIN:VEVENT
            UID:second
            DTSTART:20261020T170000Z
            DTEND:20261020T173000Z
            SUMMARY:Second
            END:VEVENT
            BEGIN:VEVENT
            UID:late
            DTSTART:20261022T160000Z
            DTEND:20261022T163000Z
            SUMMARY:Late
            END:VEVENT
            END:VCALENDAR
            """, maximumEvents: 1);

        Assert.Equal("First", Assert.Single(meetings).Title);
    }

    [Fact]
    public void ReadUpcoming_ReadsMultipleCalendarComponents()
    {
        var now = new DateTimeOffset(2026, 10, 20, 15, 30, 0, TimeSpan.Zero);
        var meetings = ReadCalendar(now, """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:first-calendar
            DTSTART:20261020T160000Z
            DTEND:20261020T163000Z
            SUMMARY:First calendar
            END:VEVENT
            END:VCALENDAR
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:second-calendar
            DTSTART:20261020T170000Z
            DTEND:20261020T173000Z
            SUMMARY:Second calendar
            END:VEVENT
            END:VCALENDAR
            """);

        Assert.Equal(new[] { "First calendar", "Second calendar" }, meetings.Select(meeting => meeting.Title));
    }

    private static IReadOnlyList<CalendarMeeting> ReadCalendar(
        DateTimeOffset now,
        string content,
        int maximumEvents = IcsCalendarReader.MaximumEvents,
        TimeSpan? window = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"biscotti-calendar-{Guid.NewGuid():N}.ics");
        try
        {
            File.WriteAllText(path, content);
            return IcsCalendarReader.ReadUpcoming(path, now, window, maximumEvents);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
