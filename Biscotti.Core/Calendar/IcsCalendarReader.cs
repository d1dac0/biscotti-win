using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace Biscotti.Core.Calendar;

/// <summary>Reads upcoming events from a local iCalendar export without network access.</summary>
public static class IcsCalendarReader
{
    public static readonly TimeSpan UpcomingWindow = TimeSpan.FromHours(24);
    public const int MaximumEvents = 5;
    private const long MaximumFileBytes = 10 * 1024 * 1024;

    public static IReadOnlyList<CalendarMeeting> ReadUpcoming(
        string path,
        DateTimeOffset now,
        TimeSpan? window = null,
        int maximumEvents = MaximumEvents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumEvents < 1) throw new ArgumentOutOfRangeException(nameof(maximumEvents));

        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("The selected calendar file was not found.", path);
        if (info.Length > MaximumFileBytes)
            throw new InvalidDataException("The selected calendar file is larger than 10 MB.");

        var content = File.ReadAllText(path);
#pragma warning disable CS0618 // CalendarCollection.Load is the supported string deserializer in iCal.NET 5.2.3.
        var calendars = CalendarCollection.Load(content);
#pragma warning restore CS0618
        if (calendars.Count == 0)
            throw new InvalidDataException("The selected file does not contain an iCalendar calendar.");

        var localZoneId = TimeZoneInfo.Local.Id;
        var from = new CalDateTime(now.LocalDateTime, localZoneId);
        var until = new CalDateTime((now + (window ?? UpcomingWindow)).LocalDateTime, localZoneId);
        var occurrences = calendars.GetOccurrences(from, new Ical.Net.Evaluation.EvaluationOptions())
            .TakeWhileBefore(until)
            .Where(o => o.Source is CalendarEvent)
            .Select(o => (Occurrence: o, Event: (CalendarEvent)o.Source))
            .Where(x => x.Event.Status != "CANCELLED")
            .Where(x => x.Occurrence.Period!.EffectiveEndTime!.AsUtc > now.UtcDateTime)
            .OrderBy(x => x.Occurrence.Period!.StartTime!.AsUtc)
            .Take(maximumEvents)
            .Select(x => new CalendarMeeting(
                EventId(x.Event, x.Occurrence),
                NormalizeTitle(x.Event.Summary),
                new DateTimeOffset(DateTime.SpecifyKind(x.Occurrence.Period!.StartTime!.AsUtc, DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(x.Occurrence.Period!.EffectiveEndTime!.AsUtc, DateTimeKind.Utc)),
                AttendeeNames(x.Event),
                NormalizeMetadata(x.Event.Description, 2000),
                NormalizeMetadata(x.Event.Location, 300)))
            .DistinctBy(meeting => meeting.EventId, StringComparer.Ordinal)
            .ToList();

        return occurrences;
    }

    private static string EventId(CalendarEvent calendarEvent, Occurrence occurrence) =>
        $"{calendarEvent.Uid ?? calendarEvent.Summary ?? "event"}:{occurrence.Period!.StartTime!.AsUtc.Ticks}";

    private static string NormalizeTitle(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return "Untitled meeting";
        var title = new string(summary.Where(character => !char.IsControl(character)).Take(160).ToArray()).Trim();
        return title.Length == 0 ? "Untitled meeting" : title;
    }

    private static IReadOnlyList<string> AttendeeNames(CalendarEvent calendarEvent) =>
        calendarEvent.Attendees
            .Select(a => a.CommonName is null
                ? null
                : new string(a.CommonName.Where(character => !char.IsControl(character)).Take(100).ToArray()).Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();

    private static string? NormalizeMetadata(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = new string(value.Where(character => !char.IsControl(character) || character is '\r' or '\n' or '\t')
            .Take(maximumLength).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
