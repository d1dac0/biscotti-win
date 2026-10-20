using Biscotti.Core.Models;

namespace Biscotti.Core;

/// <summary>Formatting helpers for meeting timestamps and elapsed durations.</summary>
public static class MeetingTimeDisplay
{
    public static DateTime ToLocalStartTime(DateTime startedUtc)
    {
        // Older or hand-edited meeting.json values may lack a UTC kind. The
        // field's contract is UTC, so preserve its clock value before converting.
        var utc = startedUtc.Kind == DateTimeKind.Utc
            ? startedUtc
            : DateTime.SpecifyKind(startedUtc, DateTimeKind.Utc);
        return utc.ToLocalTime();
    }

    public static TimeSpan Duration(Meeting meeting, DateTime? nowUtc = null)
    {
        var start = DateTime.SpecifyKind(meeting.StartedUtc, DateTimeKind.Utc);
        var end = meeting.EndedUtc is { } ended
            ? DateTime.SpecifyKind(ended, DateTimeKind.Utc)
            : DateTime.SpecifyKind(nowUtc ?? DateTime.UtcNow, DateTimeKind.Utc);
        return end - start;
    }

    public static string FormatDuration(TimeSpan duration)
    {
        var totalHours = Math.Max(0, (long)duration.TotalHours);
        return $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
