using Biscotti.Core.Ai;
using Biscotti.Core.Models;
using Xunit;

namespace Biscotti.Tests;

/// <summary>Issue #16: transcripts over one LLM-pass budget are map-reduce chunked, so late-said action items survive summarization.</summary>
public class ChunkedSummaryTests
{
    private static TranscriptLine Line(int i, string text) => new()
    {
        Speaker = i % 2 == 0 ? "You" : "Speaker 1",
        Text = text,
        StartSeconds = i * 30,
        EndSeconds = i * 30 + 20,
    };

    private static List<TranscriptLine> RoundTrip(params string[] texts) =>
        texts.Select((t, i) => Line(i, t)).ToList();

    private sealed class CapturingProgress<T> : IProgress<T>
    {
        public List<T> Reports { get; } = new();
        public void Report(T value) => Reports.Add(value);
    }

    // ---------- ChunkLines ----------

    [Fact]
    public void ShortTranscriptIsOneChunk()
    {
        var lines = RoundTrip("hello there", "second line", "third line");
        var chunks = OllamaService.ChunkLines(lines, maxChars: 10_000);
        var chunk = Assert.Single(chunks);
        Assert.Equal(3, chunk.Count);
        Assert.Equal(lines.Select(l => l.StartSeconds), chunk.Select(l => l.StartSeconds)); // time order preserved
    }

    [Fact]
    public void EmptyTranscriptProducesNoChunks()
    {
        Assert.Empty(OllamaService.ChunkLines(new List<TranscriptLine>()));
    }

    [Fact]
    public void LongTranscriptSplitsIntoBudgetFittingChunks()
    {
        var filler = new string('f', 300); // ~330 chars formatted per line
        var lines = Enumerable.Range(0, 40).Select(i => Line(i, filler)).ToList();
        var chunks = OllamaService.ChunkLines(lines, maxChars: 1000);

        Assert.True(chunks.Count > 1);
        foreach (var chunk in chunks)
            Assert.True(OllamaService.FormatTranscript(chunk).Length <= 1000); // within budget
        // every line categorized exactly once, and global time order never inverted
        var all = chunks.SelectMany(c => c).ToList();
        Assert.Equal(lines.Select(l => l.StartSeconds), all.Select(l => l.StartSeconds));
    }

    [Fact]
    public void SingleOversizedLineStillGetsItsOwnPass()
    {
        var huge = Line(0, new string('h', 5_000));
        var chunks = OllamaService.ChunkLines([huge, Line(1, "short")], maxChars: 1_000);
        Assert.Equal(2, chunks.Count);
        Assert.Equal(huge, chunks[0].Single());
        Assert.Equal("short", chunks[1].Single().Text);
    }

    // ---------- SummarizeMeetingAsync via the GenerateProbe seam ----------

    /// <summary>~29k formatted chars: over the default 24k budget, with the action item said last.</summary>
    private static List<TranscriptLine> LongMeetingWithLateActionItem()
    {
        var lines = new List<TranscriptLine>();
        var filler = "We discussed the quarterly roadmap and dependencies. " + new string('f', 160);
        for (var i = 0; i < 120; i++) lines.Add(Line(i, filler));
        lines.Add(Line(120, "ACTION-SIGNAL: as we wrap up, I will send the report by Monday"));
        return lines;
    }

    [Fact]
    public async Task LongMeetingIsChunkedAndLateActionItemSurvives()
    {
        var transcript = LongMeetingWithLateActionItem();
        var progress = new CapturingProgress<(int chunk, int total)>();
        var mapPrompts = new List<string>();

        var ollama = new OllamaService();
        ollama.GenerateProbe = (prompt, ct) =>
        {
            if (prompt.Contains("PART "))
            {
                mapPrompts.Add(prompt);
                return Task.FromResult(prompt.Contains("ACTION-SIGNAL")
                    ? "## Key Points\nwrapping up\n## Action Items\n- [ ] I will send the report by Monday"
                    : "## Key Points\nroadmap talk\n## Action Items\nnone");
            }
            // merge pass: echo every verbatim action line the part notes carried
            var body = string.Join(Environment.NewLine, prompt.Split('\n').Where(l => l.StartsWith("- [ ]")));
            return Task.FromResult($"## Summary\nComplete meeting summary.\n## Action Items\n{body}\n## Title\nQ3 Roadmap\n## Participants\nSteve, You");
        };

        var raw = await ollama.SummarizeMeetingAsync(transcript, progress);

        var expectedChunks = OllamaService.ChunkLines(transcript).Count;
        Assert.True(expectedChunks > 1);

        Assert.NotNull(raw);
        Assert.Equal(expectedChunks, mapPrompts.Count);         // one map pass per chunk
        Assert.Equal(expectedChunks, progress.Reports.Count);   // cost/progress surfaced, not hidden
        Assert.Equal((1, expectedChunks), progress.Reports[0]);
        Assert.Equal((expectedChunks, expectedChunks), progress.Reports[^1]);
        Assert.Contains("PART 1 OF", mapPrompts[0]);            // first part is the transcript start
        Assert.Contains($"PART {expectedChunks} OF", mapPrompts[^1]);
        Assert.Contains("ACTION-SIGNAL", mapPrompts[^1]);       // late material was in the last map pass
        Assert.DoesNotContain("ACTION-SIGNAL", mapPrompts[0]);
        // acceptance: items said in the last stretch land in the final summary
        Assert.Contains("## Action Items", raw!);
        Assert.Contains("I will send the report by Monday", raw);
    }

    [Fact]
    public async Task ShortMeetingStillUsesTheSinglePassPrompt()
    {
        var ollama = new OllamaService();
        var calls = 0;
        ollama.GenerateProbe = (prompt, ct) =>
        {
            calls++;
            Assert.Contains("meeting-notes assistant", prompt);   // the single-pass prompt shape
            Assert.Contains("ACTION-PROMPT-MARKER", prompt);
            return Task.FromResult("## Summary\none pass\n## Action Items\nNone\n## Title\nT\n## Participants\nYou");
        };

        var raw = await ollama.SummarizeMeetingAsync(RoundTrip("ACTION-PROMPT-MARKER"));
        Assert.Equal(1, calls); // exactly one pass, no chunking for short meetings
        Assert.Contains("## Summary", raw!);
    }
}
