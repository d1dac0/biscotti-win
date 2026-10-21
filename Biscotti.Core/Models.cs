namespace Biscotti.Core.Models;

public sealed class Meeting
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    public MeetingProcessingStatus ProcessingStatus { get; set; } = MeetingProcessingStatus.Complete;
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public string? Summary { get; set; }
    public List<string> CalendarAttendees { get; set; } = new();
    public Biscotti.Core.Calendar.CalendarMeeting? CalendarEvent { get; set; }
    public List<string> ActionItems { get; set; } = new();
    public List<TimestampedMeetingNote> TimestampedNotes { get; set; } = new();
    public List<TranscriptLine> Transcript { get; set; } = new();
}

public enum MeetingProcessingStatus
{
    Complete,
    Recording,
    Transcribing,
    TranscriptionFailed,
    TranscriptionPending,
    SummaryPending,
    Summarizing,
    SummaryFailed,
}

public sealed class TimestampedMeetingNote
{
    public required string Text { get; set; }
    public required double StartSeconds { get; set; }
}

public sealed class TranscriptLine
{
    public string? SpeakerKey { get; set; }
    public required string Speaker { get; set; }
    public required string Text { get; set; }
    public required double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
}

public enum RecordingState { Idle, Recording, Transcribing, Summarizing, Starting }
