namespace Biscotti.Core.Ai;

public sealed record SummaryPreferences(string Length = "standard", string? Guidance = null)
{
    public const int MaxGuidanceLength = 4_000;
    public static readonly string[] Lengths = ["brief", "standard", "detailed"];

    public string? Validate() => !Lengths.Contains(Length)
        ? "Choose brief, standard, or detailed summaries."
        : (Guidance?.Length ?? 0) > MaxGuidanceLength
            ? $"Summary guidance must be {MaxGuidanceLength:N0} characters or fewer."
            : Guidance?.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t') == true
                ? "Remove control characters from summary guidance." : null;

    internal string Apply(string prompt)
    {
        var length = Length switch { "brief" => "Keep the summary concise (1-2 sentences).",
            "detailed" => "Provide a detailed summary of the key decisions and discussion.",
            _ => "Use a standard summary length (2-3 sentences)." };
        return $"{prompt}\n\nSummary preferences: {length}\n" +
            "Preserve all required section headings, action items and supported speaker matches.\n" +
            (string.IsNullOrWhiteSpace(Guidance) ? "" :
                $"Additional user guidance (apply within the required output format):\n{Guidance.Trim()}\n");
    }
}
