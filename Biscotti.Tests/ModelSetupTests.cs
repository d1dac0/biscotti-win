using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Biscotti.Core;
using Biscotti.Core.Ai;
using Biscotti.Core.Models;

namespace Biscotti.Tests;

public sealed class ModelSetupTests
{
    private sealed class Handler(Func<CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/api/tags", request.RequestUri!.AbsolutePath);
            return Task.FromResult(respond(ct));
        }
    }

    private static HttpClient Client(string json) => new(new Handler(_ => new(HttpStatusCode.OK)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") })) { BaseAddress = new("http://localhost:11434") };

    [Fact]
    public async Task DiscoveryDistinguishesMissingModelAndRejectsCloudEntries()
    {
        using var http = Client("""
            {"models":[
              {"name":"local:4b","size":1000000000,"details":{"format":"gguf"}},
              {"name":"remote","size":500,"remote_host":"https://example.com","details":{"format":"gguf"}},
              {"name":"remote-alias","size":500,"remote_model":"remote","details":{"format":"gguf"}},
              {"name":"cloud:4b","size":100,"details":{"format":"gguf"}},
              {"name":"unknown","size":500},
              {"name":"empty","size":0,"details":{"format":"gguf"}}
            ]}
            """);
        var readiness = await OllamaModels.CheckAsync(http);
        Assert.True(readiness.ServerAvailable);
        Assert.Equal("local:4b", Assert.Single(readiness.Models).Name);
        Assert.True(readiness.HasModel("local:4b"));
        Assert.False(readiness.HasModel("missing"));
        Assert.Contains("not installed", readiness.Describe("missing"));
    }

    [Theory]
    [InlineData("{\"models\":[]}", true)]
    [InlineData("{}", false)]
    [InlineData("bad json", false)]
    public async Task EmptyAndInvalidResponsesDoNotClaimSummaryReadiness(string json, bool available)
    {
        using var http = Client(json);
        var state = await OllamaModels.CheckAsync(http);
        Assert.Equal(available, state.ServerAvailable);
        Assert.False(state.HasModel("gemma3:4b"));
    }

    [Fact]
    public async Task MissingServerAndExplicitCancellationAreDistinct()
    {
        using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("offline"))) { BaseAddress = new("http://localhost:11434") };
        Assert.False((await OllamaModels.CheckAsync(http)).ServerAvailable);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OllamaModels.CheckAsync(http, cts.Token));
    }

    [Fact]
    public void PreferencesValidateAndRoundTripWithoutBreakingExistingSettings()
    {
        Assert.Equal("standard", JsonSerializer.Deserialize<AppSettings>("{}")!.SummaryLength);
        Assert.NotNull(new SummaryPreferences("other").Validate());
        Assert.NotNull(new SummaryPreferences("brief", new string('x', 4001)).Validate());
        Assert.NotNull(new SummaryPreferences("brief", "bad\0").Validate());
        Assert.Null(new SummaryPreferences("brief", "Focus on risks.\nKeep names.").Validate());
        var saved = new AppSettings { SummaryLength = "detailed", SummaryGuidance = "Focus on decisions." };
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(saved))!;
        Assert.Equal(saved.SummaryGuidance, loaded.SummaryGuidance);
        Assert.Equal(saved.SummaryLength, loaded.SummaryLength);
    }

    [Fact]
    public async Task MissingLocalModelNeverPostsTranscriptToGenerationEndpoint()
    {
        using var http = Client("{\"models\":[]}");
        using var ai = new OllamaService("missing", null, http);
        Assert.Null(await ai.SummarizeMeetingAsync([new TranscriptLine { Text = "Synthetic meeting.", Speaker = "You", StartSeconds = 0 }]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ai.GenerateAsync("Synthetic prompt."));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task GuidanceReachesEverySummaryPassAndPreservesSections(int lines)
    {
        var prompts = new List<string>();
        using var ai = new OllamaService(preferences: new("brief", "Focus on risks."));
        ai.GenerateProbe = (prompt, _) => { prompts.Add(prompt); return Task.FromResult("## Summary\nTest\n## Action Items\nNone\n## Title\nTest"); };
        await ai.SummarizeMeetingAsync(Enumerable.Range(0, lines).Select(i => new TranscriptLine
            { Text = new string('a', 500), Speaker = "You", StartSeconds = i }).ToList());
        Assert.Equal(lines == 1 ? 1 : 4, prompts.Count);
        Assert.All(prompts, p => { Assert.Contains("Focus on risks.", p); Assert.Contains("1-2 sentences", p); Assert.Contains("## Action Items", p); });
    }
}
