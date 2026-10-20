using Biscotti.Core.Ai;
using Biscotti.Core.Models;

namespace Biscotti.Tests;

public sealed class CalendarSummaryContextTests
{
    [Fact]
    public async Task CalendarAttendeesAreEvidenceGatedHintsAndSpeakerMatchesParse()
    {
        var ollama = new OllamaService();
        string? capturedPrompt = null;
        ollama.GenerateProbe = (prompt, _) =>
        {
            capturedPrompt = prompt;
            return Task.FromResult("""
                ## Summary
                Design review.
                ## Action Items
                none
                ## Title
                Design review
                ## Participants
                Speaker 1: Alex Example
                """);
        };
        var transcript = new List<TranscriptLine>
        {
            new() { Speaker = "Speaker 1", Text = "Hi, I am Alex from the design team.", StartSeconds = 0, EndSeconds = 2 },
        };

        var raw = await ollama.SummarizeMeetingAsync(transcript, knownAttendees: ["Alex Example", "Sam Example"]);
        var (_, _, _, matches) = SummaryParser.Parse(raw);

        Assert.Contains("Alex Example", capturedPrompt);
        Assert.Contains("Sam Example", capturedPrompt);
        Assert.Contains("Never infer a match from the invite alone", capturedPrompt);
        Assert.Equal("Alex Example", matches["Speaker 1"]);
    }
}
