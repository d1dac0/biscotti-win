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

    public async Task<string?> SummarizeMeetingAsync(List<TranscriptLine> transcript, CancellationToken ct = default)
    {
        if (!await IsAvailableAsync(ct)) return null;
        var text = FormatTranscript(transcript, maxChars: 24_000);
        var prompt = $$"""
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
        return await GenerateAsync(prompt, ct);
    }

    public static string FormatTranscript(List<TranscriptLine> lines, int maxChars = 24_000)
    {
        var sb = new StringBuilder();
        foreach (var l in lines.OrderBy(x => x.StartSeconds))
        {
            var line = $"[{TimeSpan.FromSeconds(l.StartSeconds):hh\\:mm\\:ss}] {l.Speaker}: {l.Text.Trim()}";
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
