using System.Text;
using System.Text.Json;
using Biscotti.Core.Calendar;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;

namespace Biscotti.Tests;

public sealed class CalendarParityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 15, 0, 0, TimeSpan.Zero);
    private static CalendarMeeting Event(string id, double minutes = 0) => new(id, id, Now.AddMinutes(minutes),
        Now.AddMinutes(minutes + 60), ["Alex", "Sam"]);

    [Fact]
    public void UpcomingIncludesCurrentMeetingsAndSevenDaysAndHonorsSelection()
    {
        var current = Event("current", -30);
        var later = Event("six days", 6 * 24 * 60);
        var personal = Event("appointment") with { Attendees = ["Alex"] };
        var video = personal with { EventId = "video", Url = "https://meet.google.com/abc-defg-hij" };
        var schedule = new CalendarSchedule([], [later, personal, current, current, video,
            Event("ended", -120), Event("outside", 7 * 24 * 60), Event("all day") with { IsAllDay = true }]);
        Assert.Equal(new[] { "current", "video", "six days" }, CalendarQuery.Upcoming(schedule, Now).Select(e => e.EventId));
        Assert.Empty(CalendarQuery.Upcoming(schedule, Now, []));
        Assert.Empty(CalendarQuery.Upcoming(schedule, Now, ["another calendar"]));
    }

    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij", "Google Meet")]
    [InlineData("Join (https://company.zoom.us/j/12345).", "Zoom")]
    [InlineData("https://teams.microsoft.com/l/meetup-join/abc", "Teams")]
    [InlineData("https://company.webex.com/meet/team", "Webex")]
    [InlineData("https://meet.google.com.evil.test/abc", null)]
    [InlineData("https://evil.test@meet.google.com/abc", null)]
    [InlineData("https://meet.google.com:8443/abc", null)]
    [InlineData("https://meet.google.com/", null)]
    [InlineData("file:///C:/something.exe", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("http://meet.google.com/abc", null)]
    public void ConferenceLinksRequireRecognizedHttpsHosts(string input, string? platform) =>
        Assert.Equal(platform, ConferenceLink.Platform(ConferenceLink.Find(input)));

    [Fact]
    public void NearbyPickerIncludesNonMeetingsWithinNinetyMinutesAndRanksClosest()
    {
        var ordinary = Event("appointment", 4) with { Attendees = [] };
        var schedule = new CalendarSchedule([], [Event("early", -90), ordinary, Event("late", 90),
            Event("too early", -91), Event("all day", 0) with { IsAllDay = true }]);
        Assert.Equal(new[] { "appointment", "early", "late" }, CalendarQuery.Near(schedule, Now).Select(e => e.EventId));
        Assert.Equal("current video", CalendarQuery.BestMatch([
            Event("ordinary"), Event("current video", -20) with { Url = "https://zoom.us/j/123" },
            Event("later video", 10) with { Url = "https://zoom.us/j/456" }, Event("too late", 11)], Now)?.Title);
    }

    [Fact]
    public async Task SourceChangeDiscardsInFlightResultsAndQueriesAreSerialized()
    {
        var service = new CalendarService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<CalendarSchedule>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Configure(new Source((_, _) => { entered.SetResult(); return release.Task; }), null);
        var old = service.RefreshAsync(Now);
        await entered.Task;
        var newReads = 0;
        service.Configure(new Source((_, _) => { newReads++; return Task.FromResult(new CalendarSchedule([], [Event("new")])); }), null);
        var next = service.RefreshAsync(Now);
        Assert.Equal(0, newReads);
        release.SetResult(new([], [Event("old")]));
        Assert.False(await old);
        Assert.True(await next);
        Assert.Equal("new", Assert.Single(service.Upcoming(Now)).Title);
    }

    [Fact]
    public async Task FailedRefreshRemovesStaleOffers()
    {
        var fails = false;
        var service = new CalendarService();
        service.Configure(new Source((_, _) => fails
            ? Task.FromException<CalendarSchedule>(new IOException())
            : Task.FromResult(new CalendarSchedule([], [Event("old")]))), null);
        await service.RefreshAsync(Now);
        fails = true;
        await Assert.ThrowsAsync<IOException>(() => service.RefreshAsync(Now));
        Assert.Empty(service.Upcoming(Now));
    }

    [Fact]
    public void AssociationPersistsContextAndRollsBackFailedSavesWithoutRenamingSpeakers()
    {
        var meeting = new Meeting { Title = "My title", Transcript = [new() { Speaker = "Confirmed Sam", Text = "Hello", StartSeconds = 0 }] };
        var ev = Event("linked") with { Description = "Agenda", Location = "Room 3" };
        CalendarAssociation.Set(meeting, ev, _ => { });
        var bytes = JsonSerializer.SerializeToUtf8Bytes(meeting);
        var restored = JsonSerializer.Deserialize<Meeting>(bytes)!;
        Assert.Equal("Agenda", restored.CalendarEvent?.Description);
        Assert.Equal(new[] { "Alex", "Sam" }, restored.CalendarAttendees);
        Assert.DoesNotContain("ConferenceUrl", Encoding.UTF8.GetString(bytes));
        Assert.Throws<IOException>(() => CalendarAssociation.Set(meeting, Event("replacement"), _ => throw new IOException()));
        Assert.Same(ev, meeting.CalendarEvent);
        Assert.Equal("Confirmed Sam", meeting.Transcript[0].Speaker);
        Assert.Equal("My title", meeting.Title);
        CalendarAssociation.Set(meeting, null, _ => { });
        Assert.Null(meeting.CalendarEvent);
        Assert.Empty(meeting.CalendarAttendees);
    }

    [Fact]
    public void ReaderPreservesRunningOccurrencesAcrossWeekAndSeparatesCalendars()
    {
        var path = Path.Combine(Path.GetTempPath(), $"biscotti-calendar-parity-{Guid.NewGuid():N}.ics");
        try
        {
            File.WriteAllText(path, """
                BEGIN:VCALENDAR
                VERSION:2.0
                X-WR-CALNAME:Work
                BEGIN:VEVENT
                UID:daily
                DTSTART:20261021T143000Z
                DTEND:20261021T153000Z
                RRULE:FREQ=DAILY;COUNT=7
                SUMMARY:Daily meeting
                URL:https://meet.google.com/abc-defg-hij
                END:VEVENT
                END:VCALENDAR
                BEGIN:VCALENDAR
                VERSION:2.0
                X-WR-CALNAME:Personal
                BEGIN:VEVENT
                UID:daily
                DTSTART:20261021T160000Z
                DTEND:20261021T170000Z
                SUMMARY:Personal appointment
                END:VEVENT
                END:VCALENDAR
                """);
            var schedule = IcsCalendarReader.ReadSchedule(path, Now, Now.AddDays(7));
            Assert.Equal(new[] { "Work", "Personal" }, schedule.Calendars.Select(c => c.Title));
            Assert.Equal(8, schedule.Events.Count);
            Assert.Contains(schedule.Events, e => e.StartsAt < Now && e.EndsAt > Now);
            Assert.Equal(7, CalendarQuery.Upcoming(schedule, Now).Count);
            Assert.Empty(CalendarQuery.Upcoming(schedule, Now, [schedule.Calendars[1].Id]));
        }
        finally { File.Delete(path); }
    }

    private sealed class Source(Func<DateTimeOffset, DateTimeOffset, Task<CalendarSchedule>> read) : ICalendarSource
    {
        public Task<CalendarSchedule> ReadAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default) => read(from, until);
    }
}
