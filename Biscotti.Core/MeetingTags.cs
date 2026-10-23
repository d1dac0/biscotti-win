using Biscotti.Core.Models;

namespace Biscotti.Core;

/// <summary>
/// Meeting tags (#120): short user labels for organizing history. Tags are
/// plain meeting text — they ride the protected meeting.json like titles and
/// notes, and never leave the machine.
/// </summary>
public static class MeetingTags
{
    public const int MaxLength = 40;
    public const int MaxPerMeeting = 20;

    /// <summary>Canonical form with collapsed inner whitespace, or null when unusable.</summary>
    public static string? Normalize(string? candidate)
    {
        if (candidate is null) return null;
        var collapsed = string.Join(' ', candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length > MaxLength) collapsed = collapsed[..MaxLength].TrimEnd();
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static int IndexOf(IReadOnlyList<string> tags, string tag) =>
        tags.ToList().FindIndex(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a normalized tag unless blank, a case-insensitive duplicate, or over the per-meeting cap.</summary>
    public static bool TryAdd(Meeting meeting, string? candidate)
    {
        var tag = Normalize(candidate);
        if (tag is null || meeting.Tags.Count >= MaxPerMeeting || IndexOf(meeting.Tags, tag) >= 0) return false;
        meeting.Tags.Add(tag);
        return true;
    }

    /// <summary>Removes a tag case-insensitively; false when this meeting does not carry it.</summary>
    public static bool Remove(Meeting meeting, string tag)
    {
        var index = IndexOf(meeting.Tags, tag);
        if (index < 0) return false;
        meeting.Tags.RemoveAt(index);
        return true;
    }

    public sealed record TagSummary(string Tag, int Count)
    {
        public override string ToString() => $"{Tag} ({Count})";
    }

    /// <summary>Distinct tags with per-tag meeting counts, first-seen spelling preserved.</summary>
    public static IReadOnlyList<TagSummary> Summarize(IEnumerable<Meeting> meetings) =>
        meetings.SelectMany(m => m.Tags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TagSummary(g.First(), g.Count()))
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s.Tag, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Renames a tag on every meeting that carries it (case-insensitive). A rename onto
    /// an existing tag merges into it. Returns the meetings changed, mutated in place.
    /// </summary>
    public static IReadOnlyList<Meeting> RenameAll(IReadOnlyList<Meeting> meetings, string from, string to)
    {
        var target = Normalize(to);
        if (target is null) return Array.Empty<Meeting>();
        var affected = new List<Meeting>();
        foreach (var meeting in meetings)
        {
            var index = IndexOf(meeting.Tags, from);
            if (index < 0) continue;
            meeting.Tags.RemoveAt(index);
            if (IndexOf(meeting.Tags, target) < 0) meeting.Tags.Insert(Math.Min(index, meeting.Tags.Count), target);
            affected.Add(meeting);
        }
        return affected;
    }

    /// <summary>Removes a tag from every meeting that carries it. Returns the meetings changed.</summary>
    public static IReadOnlyList<Meeting> RemoveAll(IReadOnlyList<Meeting> meetings, string tag)
    {
        var affected = new List<Meeting>();
        foreach (var meeting in meetings)
            if (Remove(meeting, tag)) affected.Add(meeting);
        return affected;
    }
}
