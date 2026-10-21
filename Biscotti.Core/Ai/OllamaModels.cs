using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Biscotti.Core.Ai;

public sealed record LocalSummaryModel(string Name, long Size)
{
    public override string ToString() => $"{Name} ({Size / 1_000_000_000.0:0.0} GB)";
}

public sealed record OllamaReadiness(bool ServerAvailable, IReadOnlyList<LocalSummaryModel> Models, string? Error = null)
{
    public bool HasModel(string? name) => Models.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    public string Describe(string? selected) => !ServerAvailable
        ? "Ollama is unavailable. Start Ollama on this PC, then retry. Transcription still works."
        : HasModel(selected) ? $"Summaries ready: {selected}. Runs offline on this PC."
        : $"Summary model {selected} is not installed locally. Choose an installed model in Settings. Transcription still works.";
}

public static class OllamaModels
{
    private sealed record Details([property: JsonPropertyName("format")] string? Format);
    private sealed record Model([property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("details")] Details? Details,
        [property: JsonPropertyName("remote_host")] string? RemoteHost,
        [property: JsonPropertyName("remote_model")] string? RemoteModel);
    private sealed record ModelList([property: JsonPropertyName("models")] Model[]? Models);

    public static async Task<OllamaReadiness> CheckAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromSeconds(5) };
        return await CheckAsync(http, ct);
    }

    internal static async Task<OllamaReadiness> CheckAsync(HttpClient http, CancellationToken ct = default)
    {
        try
        {
            var response = await http.GetFromJsonAsync<ModelList>("/api/tags", ct);
            if (response?.Models is null) return new(false, [], "Ollama returned an invalid model list.");
            var local = response.Models.Where(m => !string.IsNullOrWhiteSpace(m.Name) && m.Size > 0
                && m.Details?.Format == "gguf" && string.IsNullOrEmpty(m.RemoteHost)
                && string.IsNullOrEmpty(m.RemoteModel) && !m.Name.Contains("cloud", StringComparison.OrdinalIgnoreCase))
                .Select(m => new LocalSummaryModel(m.Name, m.Size)).OrderBy(m => m.Name).ToArray();
            return new(true, local);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        { return new(false, [], ex.Message); }
    }
}
