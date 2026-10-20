using Biscotti.Core.Models;

namespace Biscotti.Core.Transcription;

public sealed record SpeakerGroup(string Key, string Label, bool CanRemember, double StartSeconds)
{
    public string Display => $"{Label} · first turn {TimeSpan.FromSeconds(StartSeconds):hh\\:mm\\:ss}";
}
public sealed record RememberedVoice(Guid PersonId, string Name, Guid MeetingId, string SpeakerKey, float[][] Samples);
public sealed record SpeakerSuggestion(Guid PersonId, string Name);

/// <summary>Explicit labels and conservative, uncalibrated suggestions; never automatic identity.</summary>
public static class SpeakerMemory
{
    public const string ModelVersion = SpeakerEmbeddingModelStore.Sha256;
    public static List<SpeakerGroup> Groups(Meeting meeting) => meeting.Transcript
        .GroupBy(GroupKey).Select(group => new SpeakerGroup(group.Key, group.First().Speaker,
            group.All(line => line.SpeakerKey is not null && line.SpeakerKey.StartsWith("system:", StringComparison.Ordinal)),
            group.Min(line => line.StartSeconds)))
        .ToList();

    public static string GroupKey(TranscriptLine line) => line.SpeakerKey ?? "legacy:" + line.Speaker;

    public static void Correct(Meeting meeting, string key, string name)
    {
        name = ValidateName(name);
        var lines = meeting.Transcript.Where(line => GroupKey(line) == key).ToList();
        if (lines.Count == 0) throw new ArgumentException("Select an existing speaker.");
        // Freeze legacy keys before renaming so later edits cannot combine groups.
        foreach (var line in lines)
        {
            line.SpeakerKey ??= key;
            line.Speaker = name;
        }
    }

    public static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Enter a person label of 1–80 characters without control characters.");
        return name;
    }

    public static float[][] PrepareSamples(IEnumerable<float[]?> samples) => samples
        .Where(IsUsable).Take(5).Select(sample => Normalize(sample!)).ToArray();

    public static bool IsUsable(float[]? sample) => sample is { Length: > 0 and <= 4096 } &&
        sample.All(float.IsFinite) && sample.Any(value => value != 0);

    private static float[] Normalize(float[] sample)
    {
        var magnitude = Math.Sqrt(sample.Sum(value => (double)value * value));
        return sample.Select(value => (float)(value / magnitude)).ToArray();
    }

    public static SpeakerSuggestion? Suggest(Guid meetingId, IReadOnlyList<float[]> samples,
        IReadOnlyList<RememberedVoice> voices)
    {
        if (samples.Count < 2 || samples.Any(sample => !IsUsable(sample)) ||
            samples.Select(sample => sample.Length).Distinct().Count() != 1) return null;
        var people = voices.Where(voice => voice.MeetingId != meetingId).GroupBy(voice => voice.PersonId).ToList();
        Guid? agreed = null;
        string? name = null;
        foreach (var sample in samples)
        {
            var scores = people.Select(person => new
            {
                Id = person.Key,
                Name = person.First().Name,
                Score = person.SelectMany(voice => voice.Samples)
                    .Where(reference => IsUsable(reference) && reference.Length == sample.Length)
                    .Select(reference => SpeakerEmbeddingClusterer.CosineSimilarity(sample, reference))
                    .DefaultIfEmpty(-1).Max(),
            }).OrderByDescending(person => person.Score).ToList();
            if (scores.Count == 0 || scores[0].Score < 0.94f ||
                (scores.Count > 1 && scores[0].Score - scores[1].Score < 0.08f) ||
                (agreed is not null && agreed != scores[0].Id)) return null;
            agreed = scores[0].Id;
            name = scores[0].Name;
        }
        return agreed is null ? null : new SpeakerSuggestion(agreed.Value, name!);
    }
}
