using System.Text.RegularExpressions;
using Biscotti.Core.Models;

namespace Biscotti.Core.Transcription;

/// <summary>Renames a diarized voice only when the transcript contains a self-introduction.</summary>
public static partial class SpeakerIntroductionResolver
{
    private static readonly HashSet<string> NonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "able", "about", "all", "calling", "doing", "fine", "going", "good",
        "and", "here", "just", "looking", "new", "not", "okay", "ready", "trying", "well",
    };

    [GeneratedRegex(@"\b(?:my name is|i am|i'm|this is)\s+(?<name>[\p{L}][\p{L}'’.-]*(?:\s+[\p{L}][\p{L}'’.-]*)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IntroductionPattern();

    public static void Apply(IReadOnlyList<TranscriptLine> transcript)
    {
        var namesBySpeaker = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in transcript)
        {
            if (line.Speaker.Equals("You", StringComparison.OrdinalIgnoreCase)) continue;
            var match = IntroductionPattern().Match(line.Text);
            if (!match.Success) continue;

            var name = TrimTrailingConjunction(match.Groups["name"].Value);
            var firstName = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstName) || !char.IsUpper(firstName[0]) || NonNames.Contains(firstName)) continue;
            if (!namesBySpeaker.TryGetValue(line.Speaker, out var names))
                namesBySpeaker[line.Speaker] = names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            names.Add(name);
        }

        foreach (var (speaker, names) in namesBySpeaker)
            if (names.Count == 1)
            {
                var name = names.Single();
                foreach (var line in transcript.Where(line => line.Speaker.Equals(speaker, StringComparison.OrdinalIgnoreCase)))
                    line.Speaker = name;
            }
    }

    private static string TrimTrailingConjunction(string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keep = new List<string>(2);
        foreach (var word in words)
        {
            if (keep.Count > 0 && NonNames.Contains(word)) break;
            keep.Add(word.Trim(',', '.', ';', ':', '!', '?'));
            if (keep.Count == 2) break;
        }
        return string.Join(' ', keep).Trim();
    }
}
