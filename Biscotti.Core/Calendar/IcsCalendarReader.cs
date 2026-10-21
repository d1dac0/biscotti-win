using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace Biscotti.Core.Calendar;

public static class IcsCalendarReader
{
    public static readonly TimeSpan UpcomingWindow = CalendarQuery.UpcomingWindow;
    public const int MaximumEvents = 500;
    private const long MaximumFileBytes = 10 * 1024 * 1024;

    public static IReadOnlyList<CalendarMeeting> ReadUpcoming(string path, DateTimeOffset now,
        TimeSpan? window = null, int maximumEvents = MaximumEvents)
    {
        if (maximumEvents < 1) throw new ArgumentOutOfRangeException(nameof(maximumEvents));
        return ReadSchedule(path, now, now + (window ?? UpcomingWindow)).Events
            .OrderBy(e => e.StartsAt).Take(maximumEvents).ToArray();
    }

    public static CalendarSchedule ReadSchedule(string path, DateTimeOffset from, DateTimeOffset until)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (until <= from) throw new ArgumentOutOfRangeException(nameof(until));
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("The selected calendar file was not found.", path);
        if (info.Length > MaximumFileBytes) throw new InvalidDataException("The selected calendar file is larger than 10 MB.");
        var content = File.ReadAllText(path);
        return ReadContent(content, from, until, Path.GetFullPath(path).ToUpperInvariant());
    }

    public static CalendarSchedule ReadContent(string content, DateTimeOffset from, DateTimeOffset until, string sourceKey)
    {
        content = content.TrimStart('\uFEFF');
        if (until <= from) throw new ArgumentOutOfRangeException(nameof(until));
        if (Encoding.UTF8.GetByteCount(content) > MaximumFileBytes) throw new InvalidDataException("Calendar data is larger than 10 MB.");
#pragma warning disable CS0618
        var calendars = CalendarCollection.Load(content);
#pragma warning restore CS0618
        if (calendars.Count == 0) throw new InvalidDataException("The selected file does not contain an iCalendar calendar.");
        var labels = Regex.Split(content, "BEGIN:VCALENDAR", RegexOptions.IgnoreCase).Skip(1)
            .Select(part => Regex.Match(part, @"(?im)^X-WR-CALNAME(?:;[^:]*)?:(.*)$").Groups[1].Value.Trim()).ToArray();
        var zone = TimeZoneInfo.Local.Id;
        var start = new CalDateTime(from.LocalDateTime, zone);
        var end = new CalDateTime(until.LocalDateTime, zone);
        var events = new List<CalendarMeeting>();
        var sources = new List<CalendarInfo>();
        for (var i = 0; i < calendars.Count; i++)
        {
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey)))[..16];
            var id = $"ics:{fingerprint}:{i}";
            var title = i < labels.Length && labels[i].Length > 0 ? Clean(labels[i], 160)! : $"Calendar {i + 1}";
            sources.Add(new(id, title, "Local calendar file"));
            var occurrences = calendars[i].GetOccurrences(start, new Ical.Net.Evaluation.EvaluationOptions())
                .TakeWhileBefore(end).Where(o => o.Source is CalendarEvent)
                .Select(o => (Occurrence: o, Event: (CalendarEvent)o.Source))
                .Where(x => !string.Equals(x.Event.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
                    && x.Occurrence.Period!.EffectiveEndTime!.AsUtc > from.UtcDateTime).Take(2000);
            foreach (var (occurrence, ev) in occurrences)
            {
                var begins = new DateTimeOffset(DateTime.SpecifyKind(occurrence.Period!.StartTime!.AsUtc, DateTimeKind.Utc));
                var ends = new DateTimeOffset(DateTime.SpecifyKind(occurrence.Period.EffectiveEndTime!.AsUtc, DateTimeKind.Utc));
                events.Add(new CalendarMeeting(
                    $"{id}:{ev.Uid ?? ev.Summary ?? "event"}:{begins.UtcTicks}",
                    Clean(ev.Summary, 160) ?? "Untitled meeting", begins, ends,
                    ev.Attendees.Select(a => Clean(a.CommonName, 100) ?? Clean(a.Value?.ToString()?.Replace("mailto:", "", StringComparison.OrdinalIgnoreCase), 100))
                        .Where(n => n is not null).Select(n => n!).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToArray(),
                    Clean(ev.Description, 2000), Clean(ev.Location, 300), id, title, Clean(ev.Url?.ToString(), 2000),
                    ev.IsAllDay, ev.Attendees.Count));
            }
        }
        return new(sources, events.DistinctBy(e => e.EventId).OrderBy(e => e.StartsAt).ToArray());
    }

    public static string? Clean(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = new string(value.Where(c => !char.IsControl(c) || c is '\r' or '\n' or '\t').Take(maximumLength).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
