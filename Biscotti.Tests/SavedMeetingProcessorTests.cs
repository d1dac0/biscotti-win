using Biscotti.Core;
using Biscotti.Core.Models;

namespace Biscotti.Tests;

public sealed class SavedMeetingProcessorTests
{
    [Fact]
    public async Task InterruptedRecordingRecoversOnlyWhenProtectedAudioIsAvailable()
    {
        var savedStatuses = new List<MeetingProcessingStatus>();
        var processor = CreateProcessor(save: meeting => savedStatuses.Add(meeting.ProcessingStatus), hasAudio: _ => true);
        var meeting = new Meeting { ProcessingStatus = MeetingProcessingStatus.Recording };

        await processor.RecoverInterruptedAsync(meeting);

        Assert.NotNull(meeting.EndedUtc);
        Assert.Equal(MeetingProcessingStatus.TranscriptionPending, meeting.ProcessingStatus);
        Assert.Equal([MeetingProcessingStatus.TranscriptionPending], savedStatuses);

        var missing = new Meeting { ProcessingStatus = MeetingProcessingStatus.Recording };
        var noAudioProcessor = CreateProcessor(hasAudio: _ => false);
        await Assert.ThrowsAsync<InvalidDataException>(() => noAudioProcessor.RecoverInterruptedAsync(missing));
        Assert.Null(missing.EndedUtc);
        Assert.Equal(MeetingProcessingStatus.Recording, missing.ProcessingStatus);
    }

    [Fact]
    public async Task TranscriptionRetryReplacesLinesOnlyAfterSuccessAndPreservesOtherEdits()
    {
        var previousLines = Lines("Old transcript");
        var meeting = new Meeting
        {
            EndedUtc = DateTime.UtcNow,
            ProcessingStatus = MeetingProcessingStatus.TranscriptionFailed,
            Title = "My edited title", Notes = "My notes", Summary = "Previous useful summary",
            ActionItems = ["Previous action"], Transcript = previousLines,
        };
        var processor = CreateProcessor(transcribe: (_, _) => Task.FromResult(Lines("Replacement transcript")));

        await processor.RetryTranscriptionAsync(meeting);

        Assert.Equal("Replacement transcript", Assert.Single(meeting.Transcript).Text);
        Assert.NotSame(previousLines, meeting.Transcript);
        Assert.Equal("My edited title", meeting.Title);
        Assert.Equal("My notes", meeting.Notes);
        Assert.Equal("Previous useful summary", meeting.Summary);
        Assert.Equal(["Previous action"], meeting.ActionItems);
        Assert.Equal(MeetingProcessingStatus.SummaryPending, meeting.ProcessingStatus);
    }

    [Fact]
    public async Task FailedTranscriptionKeepsThePreviousTranscriptAndReportsFailure()
    {
        var previousLines = Lines("Last good transcript");
        var meeting = new Meeting { EndedUtc = DateTime.UtcNow, Transcript = previousLines };
        var processor = CreateProcessor(transcribe: (_, _) => throw new FileNotFoundException("model missing"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => processor.RetryTranscriptionAsync(meeting));

        Assert.Same(previousLines, meeting.Transcript);
        Assert.Equal("Last good transcript", Assert.Single(meeting.Transcript).Text);
        Assert.Equal(MeetingProcessingStatus.TranscriptionFailed, meeting.ProcessingStatus);
    }

    [Fact]
    public async Task StorageFailureDuringRetryKeepsPreviouslySavedTranscript()
    {
        var previousLines = Lines("Last saved transcript");
        var meeting = new Meeting { EndedUtc = DateTime.UtcNow, Transcript = previousLines };
        var processor = CreateProcessor(
            transcribe: (_, _) => Task.FromResult(Lines("Replacement transcript")),
            save: _ => throw new UnauthorizedAccessException("storage denied"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => processor.RetryTranscriptionAsync(meeting));

        Assert.Same(previousLines, meeting.Transcript);
        Assert.Equal("Last saved transcript", Assert.Single(meeting.Transcript).Text);
        Assert.Equal(MeetingProcessingStatus.TranscriptionFailed, meeting.ProcessingStatus);
    }

    [Fact]
    public async Task SummaryRetryUpdatesSummaryAndActionsButKeepsAnEditedTitle()
    {
        var meeting = new Meeting
        {
            Title = "User's title", Notes = "Private notes", Summary = "Old summary",
            ActionItems = ["Old action"], Transcript = Lines("Transcript"),
        };
        var processor = CreateProcessor(summarize: (_, _) => Task.FromResult(
            new SavedMeetingProcessor.SummaryResult("New summary", ["New action"], "Model title")));

        await processor.RegenerateSummaryAsync(meeting);

        Assert.Equal("New summary", meeting.Summary);
        Assert.Equal(["New action"], meeting.ActionItems);
        Assert.Equal("User's title", meeting.Title);
        Assert.Equal("Private notes", meeting.Notes);
        Assert.Equal(MeetingProcessingStatus.Complete, meeting.ProcessingStatus);
    }

    [Fact]
    public async Task FailedSummaryRetryRetainsTheLastGoodResultAndUserTitle()
    {
        var meeting = new Meeting
        {
            Title = "User's title", Summary = "Last good summary", ActionItems = ["Last good action"],
            Transcript = Lines("Transcript"),
        };
        var processor = CreateProcessor(summarize: (_, _) => throw new HttpRequestException("Ollama unavailable"));

        await Assert.ThrowsAsync<HttpRequestException>(() => processor.RegenerateSummaryAsync(meeting));

        Assert.Equal("Last good summary", meeting.Summary);
        Assert.Equal(["Last good action"], meeting.ActionItems);
        Assert.Equal("User's title", meeting.Title);
        Assert.Equal(MeetingProcessingStatus.SummaryFailed, meeting.ProcessingStatus);
    }

    [Fact]
    public async Task EmptyReplacementIsTreatedAsFailureAndPreservesPriorContent()
    {
        var transcript = Lines("Last good transcript");
        var meeting = new Meeting { EndedUtc = DateTime.UtcNow, Transcript = transcript };
        var processor = CreateProcessor(transcribe: (_, _) => Task.FromResult(new List<TranscriptLine>()));

        await Assert.ThrowsAsync<InvalidDataException>(() => processor.RetryTranscriptionAsync(meeting));

        Assert.Same(transcript, meeting.Transcript);
        Assert.Equal(MeetingProcessingStatus.TranscriptionFailed, meeting.ProcessingStatus);
    }

    private static SavedMeetingProcessor CreateProcessor(
        Func<Meeting, CancellationToken, Task<List<TranscriptLine>>>? transcribe = null,
        Func<Meeting, CancellationToken, Task<SavedMeetingProcessor.SummaryResult>>? summarize = null,
        Action<Meeting>? save = null,
        Func<Meeting, bool>? hasAudio = null) =>
        new(new AppSettings(), transcribe, summarize, save ?? (_ => { }), hasAudio ?? (_ => true));

    private static List<TranscriptLine> Lines(string text) =>
    [new() { Speaker = "You", Text = text, StartSeconds = 0, EndSeconds = 2 }];
}
