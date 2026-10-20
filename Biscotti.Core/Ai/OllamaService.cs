using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Biscotti.Core.Models;

namespace Biscotti.Core.Ai;

/// <summary>
/// Local LLM summaries via Ollama (http://localhost:11434). Nothing is sent to
/// the internet; if Ollama isn't running, we degrade to a plain transcript.
/// </summary>
public sealed class OllamaService : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromMinutes(10) };
    private readonly string _model;

    public OllamaService(string? model = null)
    {
        _model = string.IsNullOrWhiteSpace(model) ? "gemma3:4b" : model;
    }

    public sealed record OllamaResponse([property: JsonPropertyName("response")] string? Response);

    /// <summary>
    /// Character budget for one LLM pass (issue #16). The transcript is never
    /// silently cut at this limit any more: anything over one budget becomes
    /// map-reduce chunk passes plus a merge pass. Rough local-model cost: a
    /// 2-hour meeting (~90k transcript chars) is ~4 chunk passes + 1 merge at
    /// ~24k chars (~6k tokens) input each.
    /// </summary>
    public const int ChunkBudgetChars = 24_000;

    internal Func<string, CancellationToken, Task<string>>? GenerateProbe; // test seam (issue #16)

    /// <summary>
    /// Test seam (issue #40) for the availability gate. Only consulted when no
    /// GenerateProbe is installed: a probe replaces every network interaction,
    /// so it must not depend on a live Ollama either.
    /// </summary>
    internal Func<CancellationToken, Task<bool>>? AvailabilityProbe;

    private Task<string> Generate(string prompt, CancellationToken ct) =>
        GenerateProbe != null ? GenerateProbe(prompt, ct) : GenerateAsync(prompt, ct);

    private Task<bool> CheckAvailable(CancellationToken ct) =>
        AvailabilityProbe != null ? AvailabilityProbe(ct) : IsAvailableAsync(ct);

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("/api/tags", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync("/api/generate", new
        {
            model = _model,
            prompt,
            stream = false,
        }, cancellationToken: ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: ct);
        return (body?.Response ?? "").Trim();
    }

    public async Task<string?> SummarizeMeetingAsync(
        List<TranscriptLine> transcript,
        IProgress<(int chunk, int total)>? chunkProgress = null,
        CancellationToken ct = default)
    {
        // Issue #40: a GenerateProbe replaces every network interaction, so
        // the availability gate must not block probe-backed (test) runs when
        // no Ollama server is listening — CI has none.
        if (GenerateProbe is null && !await CheckAvailable(ct)) return null;

        var chunks = ChunkLines(transcript);
        if (chunks.Count == 1)
            return await Generate(BuildSinglePassPrompt(FormatTranscript(chunks[0])), ct);

        // Long meeting: map-reduce (issue #16). Each chunk gets its own pass,
        // then a merge pass combines the part notes — never dropping an action
        // item just because it was said late in the meeting.
        var partNotes = new List<string>(chunks.Count);
        for (var k = 0; k < chunks.Count; k++)
        {
            chunkProgress?.Report((k + 1, chunks.Count));
            partNotes.Add(await Generate(BuildChunkPrompt(k + 1, chunks.Count, FormatTranscript(chunks[k])), ct));
        }
        var merged = string.Join("\n\n", partNotes);
        return await Generate(BuildMergePrompt(chunks.Count, merged), ct);
    }

    /// <summary>
    /// Splits the transcript into time-ordered chunks whose formatted text
    /// each fits within <paramref name="maxChars"/>, so the LLM sees the whole
    /// meeting across passes instead of only its first 24 000 characters.
    /// </summary>
    public static List<List<TranscriptLine>> ChunkLines(List<TranscriptLine> lines, int maxChars = ChunkBudgetChars)
    {
        var chunks = new List<List<TranscriptLine>>();
        var current = new List<TranscriptLine>();
        var currentLen = 0;
        foreach (var l in lines.OrderBy(x => x.StartSeconds))
        {
            var len = FormattedLine(l).Length + Environment.NewLine.Length;
            if (currentLen > 0 && currentLen + len > maxChars)
            {
                chunks.Add(current);
                current = new List<TranscriptLine>();
                currentLen = 0;
            }
            current.Add(l);
            currentLen += len; // an oversized single line still gets its own pass
        }
        if (current.Count > 0) chunks.Add(current);
        return chunks;
    }

    private static string FormattedLine(TranscriptLine l) =>
        $"[{TimeSpan.FromSeconds(l.StartSeconds):hh\\:mm\\:ss}] {l.Speaker}: {l.Text.Trim()}";

    private static string BuildSinglePassPrompt(string text) => $$"""
        You are a meeting-notes assistant. Given the transcript below, produce:
        1. A 2-3 sentence summary of the meeting.
        2. A bulleted list of concrete action items with owners if stated ("- [ ] Owner: task").
        3. The most likely meeting title (short).
        4. Your best guess at participants' real names if any were said aloud (e.g. "Hi, I'm Steve").

        Format exactly as:
        ## Summary
        ...
        ## Action Items
        ...
        ## Title
        ...
        ## Participants
        ...

        Transcript (labels "You" = person at this PC, "Speaker N" = other participant):
        {{text}}
        """;

    private static string BuildChunkPrompt(int part, int total, string text) => $$"""
        You are a meeting-notes assistant. This is PART {{part}} OF {{total}} of one long meeting transcript.
        Produce dense notes for ONLY this part:
        1. 1-3 sentences of the key points said in this part.
        2. A bulleted list of the concrete commitments or action items stated in this part, with owners if stated ("- [ ] Owner: task"). Copy tasks verbatim; if there are none, write "none".

        Format exactly as:
        ## Key Points
        ...
        ## Action Items
        ...

        Part {{part}} of {{total}} transcript (labels "You" = person at this PC, "Speaker N" = other participant):
        {{text}}
        """;

    private static string BuildMergePrompt(int total, string partNotes) => $$"""
        You are a meeting-notes assistant. Below are notes produced from {{total}} sequential parts of ONE meeting.
        Merge them into the final meeting notes. Nothing said in any part may be missed — especially action items and commitments from the later parts.

        Format exactly as:
        ## Summary
        ...
        ## Action Items
        ...
        ## Title
        ...
        ## Participants
        ...

        Part notes (in order):
        {{partNotes}}
        """;

    public static string FormatTranscript(List<TranscriptLine> lines, int maxChars = ChunkBudgetChars)
    {
        var sb = new StringBuilder();
        foreach (var l in lines.OrderBy(x => x.StartSeconds))
        {
            var line = FormattedLine(l);
            if (sb.Length + line.Length > maxChars) break;
            sb.AppendLine(line);
        }
        return sb.ToString();
    }

    public void Dispose() => _http.Dispose();
}

public static class SummaryParser
{
    public static (string? summary, List<string> actions, string? title) Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, new List<string>(), null);
        static string Section(string source, string marker)
        {
            var idx = source.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            var rest = source[(idx + marker.Length)..];
            var next = rest.IndexOf("##", StringComparison.Ordinal);
            return (next >= 0 ? rest[..next] : rest).Trim();
        }
        var summary = Section(raw!, "Summary");
        var title = Section(raw!, "Title").Split('\n', 2)[0].Trim();
        var actions = Section(raw!, "Action Items")
            .Split('\n')
            .Select(l => l.Trim().TrimStart('-', '*', '[', ']', ' '))
            .Where(l => l.Length > 2)
            .ToList();
        return (summary, actions, title);
    }
}
