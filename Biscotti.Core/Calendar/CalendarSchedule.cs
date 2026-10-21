namespace Biscotti.Core.Calendar;

public sealed record CalendarInfo(string Id, string Title, string Source);
public sealed record CalendarSchedule(IReadOnlyList<CalendarInfo> Calendars, IReadOnlyList<CalendarMeeting> Events,
    IReadOnlyList<string>? Warnings = null);

public interface ICalendarSource
{
    Task<CalendarSchedule> ReadAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default);
}

public static class CalendarQuery
{
    public static readonly TimeSpan UpcomingWindow = TimeSpan.FromDays(7);
    public static IReadOnlyList<CalendarMeeting> Upcoming(CalendarSchedule schedule, DateTimeOffset now,
        IReadOnlyCollection<string>? enabledIds = null) => schedule.Events
        .Where(e => e.IsMeetingLike && e.EndsAt > now && e.StartsAt < now + UpcomingWindow
            && (enabledIds is null || enabledIds.Contains(e.CalendarId)))
        .DistinctBy(e => e.EventId).OrderBy(e => e.StartsAt).Take(500).ToArray();

    public static IReadOnlyList<CalendarMeeting> Near(CalendarSchedule schedule, DateTimeOffset date,
        IReadOnlyCollection<string>? enabledIds = null) => schedule.Events
        .Where(e => !e.IsAllDay && Math.Abs((e.StartsAt - date).TotalMinutes) <= 90
            && (enabledIds is null || enabledIds.Contains(e.CalendarId)))
        .DistinctBy(e => e.EventId).OrderBy(e => Math.Abs((e.StartsAt - date).TotalSeconds)).ToArray();

    public static CalendarMeeting? BestMatch(IEnumerable<CalendarMeeting> events, DateTimeOffset date) => events
        .Where(e => e.IsMeetingLike && e.EndsAt > date && e.StartsAt <= date.AddMinutes(10))
        .OrderByDescending(e => (e.ConferenceUrl is null ? 0 : 2) + (e.StartsAt <= date ? 1 : 0))
        .ThenBy(e => Math.Abs((e.StartsAt - date).TotalSeconds)).FirstOrDefault();
}

public sealed class IcsCalendarSource(string path) : ICalendarSource
{
    public Task<CalendarSchedule> ReadAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default) =>
        Task.Run(() => IcsCalendarReader.ReadSchedule(path, from, until), ct);
}
