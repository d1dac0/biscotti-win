using System.Net.Http;
using System.Security.Cryptography;
using Biscotti.Core.Ai;
using Biscotti.Core.Audio;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;
using Biscotti.Core.Transcription;

namespace Biscotti.Core;

/// <summary>Explicit, staged recovery for audio and processing results already saved locally.</summary>
public sealed class SavedMeetingProcessor
{
    private readonly Func<Meeting, CancellationToken, Task<List<TranscriptLine>>> _transcribe;
    private readonly Func<Meeting, CancellationToken, Task<SummaryResult>> _summarize;
    private readonly Action<Meeting> _save;
    private readonly Func<Meeting, bool> _hasRecoverableAudio;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public sealed record SummaryResult(string Summary, List<string> ActionItems, string? SuggestedTitle);

    public SavedMeetingProcessor(AppSettings settings)
        : this(settings, null, null, MeetingStore.Save, HasRecoverableAudio) { }

    internal SavedMeetingProcessor(AppSettings settings,
        Func<Meeting, CancellationToken, Task<List<TranscriptLine>>>? transcribe,
        Func<Meeting, CancellationToken, Task<SummaryResult>>? summarize,
        Action<Meeting> save, Func<Meeting, bool> hasRecoverableAudio)
    {
        _transcribe = transcribe ?? ((meeting, ct) => TranscribeLocallyAsync(meeting, settings, ct));
        _summarize = summarize ?? ((meeting, ct) => SummarizeLocallyAsync(meeting, settings, ct));
        _save = save;
        _hasRecoverableAudio = hasRecoverableAudio;
    }

    public async Task RecoverInterruptedAsync(Meeting meeting, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (meeting.ProcessingStatus != MeetingProcessingStatus.Recording)
                throw new InvalidOperationException("This meeting is not marked as an interrupted recording.");
            if (!_hasRecoverableAudio(meeting))
                throw new InvalidDataException("No complete protected audio frames could be recovered. The meeting data was left unchanged.");

            var priorEnded = meeting.EndedUtc;
            var priorStatus = meeting.ProcessingStatus;
            meeting.EndedUtc ??= DateTime.UtcNow;
            meeting.ProcessingStatus = MeetingProcessingStatus.TranscriptionPending;
            try { _save(meeting); }
            catch { meeting.EndedUtc = priorEnded; meeting.ProcessingStatus = priorStatus; throw; }
        }
        finally { _gate.Release(); }
    }

    public async Task RetryTranscriptionAsync(Meeting meeting, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        var priorLines = meeting.Transcript;
        try
        {
            if (meeting.EndedUtc is null)
                throw new InvalidOperationException("Recover the interrupted recording before retrying transcription.");
            meeting.ProcessingStatus = MeetingProcessingStatus.Transcribing;
            _save(meeting);
            var replacement = await _transcribe(meeting, ct);
            if (replacement.Count == 0) throw new InvalidDataException("The saved audio did not produce any transcript lines. The previous transcript was kept.");
            meeting.Transcript = replacement;
            meeting.ProcessingStatus = MeetingProcessingStatus.SummaryPending;
            _save(meeting);
        }
        catch
        {
            meeting.Transcript = priorLines;
            meeting.ProcessingStatus = MeetingProcessingStatus.TranscriptionFailed;
            try { _save(meeting); } catch { /* retain the original processing error and in-memory transcript */ }
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task RegenerateSummaryAsync(Meeting meeting, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        var priorSummary = meeting.Summary;
        var priorActions = meeting.ActionItems;
        var priorTitle = meeting.Title;
        try
        {
            if (meeting.Transcript.Count == 0)
                throw new InvalidOperationException("Transcribe the saved audio before generating a summary.");
            meeting.ProcessingStatus = MeetingProcessingStatus.Summarizing;
            _save(meeting);
            var replacement = await _summarize(meeting, ct);
            if (string.IsNullOrWhiteSpace(replacement.Summary))
                throw new InvalidDataException("The local summary model returned no summary. Any previous summary was kept.");
            meeting.Summary = replacement.Summary;
            meeting.ActionItems = replacement.ActionItems;
            if (string.IsNullOrWhiteSpace(meeting.Title) && !string.IsNullOrWhiteSpace(replacement.SuggestedTitle) && replacement.SuggestedTitle.Length < 80)
                meeting.Title = replacement.SuggestedTitle;
            meeting.ProcessingStatus = MeetingProcessingStatus.Complete;
            _save(meeting);
        }
        catch
        {
            meeting.Summary = priorSummary;
            meeting.ActionItems = priorActions;
            meeting.Title = priorTitle;
            meeting.ProcessingStatus = MeetingProcessingStatus.SummaryFailed;
            try { _save(meeting); } catch { /* preserve the user's previous result in memory */ }
            throw;
        }
        finally { _gate.Release(); }
    }

    private static async Task<List<TranscriptLine>> TranscribeLocallyAsync(Meeting meeting, AppSettings settings, CancellationToken ct)
    {
        var directory = MeetingStore.MeetingDir(meeting.Id);
        var micPath = Path.Combine(directory, "mic.wav");
        var sysPath = Path.Combine(directory, "sys.wav");
        using var transcriber = new WhisperTranscriber(settings.GetResolvedModelPath(), settings.LocalVocabulary);
        var mic = await TranscribeChannelIfPresentAsync(transcriber, micPath, "You", ct);
        var sys = await TranscribeChannelIfPresentAsync(transcriber, sysPath, "Speaker", ct);
        return TranscriptAssembler.Merge(mic, sys);
    }

    private static Task<List<TranscriptLine>> TranscribeChannelIfPresentAsync(WhisperTranscriber transcriber,
        string path, string speaker, CancellationToken ct)
    {
        if (!File.Exists(path)) return Task.FromResult(new List<TranscriptLine>());
        if (!EncryptedAudioFile.IsEncrypted(path))
            throw new InvalidDataException($"{MeetingAudioMigration.AudioIdentity(path)} does not have a protected audio header.");
        return TranscribeAudioAsync(transcriber, path, speaker, ct);
    }

    private static async Task<List<TranscriptLine>> TranscribeAudioAsync(WhisperTranscriber transcriber,
        string path, string speaker, CancellationToken ct)
    {
        EncryptedAudioReader reader;
        try { reader = new EncryptedAudioReader(path); }
        catch (CryptographicException ex) { throw MeetingAudioMigration.KeyAccessFailure(path, ex); }
        using (reader)
        {
            if (reader.PcmLength == 0) return [];
            using var stream = reader.OpenWavStream();
            return await transcriber.TranscribeWavAsync(stream, speaker, ct);
        }
    }

    private static async Task<SummaryResult> SummarizeLocallyAsync(Meeting meeting, AppSettings settings, CancellationToken ct)
    {
        using var ollama = new OllamaService(settings.OllamaModel,
            new SummaryPreferences(settings.SummaryLength, settings.SummaryGuidance));
        var raw = await ollama.SummarizeMeetingAsync(meeting.Transcript, ct: ct,
            knownAttendees: meeting.CalendarAttendees, calendarMeeting: meeting.CalendarEvent);
        if (string.IsNullOrWhiteSpace(raw))
            throw new HttpRequestException("Local Ollama is unavailable or returned no summary.");
        var (summary, actions, title, _) = SummaryParser.Parse(raw);
        return new(summary ?? string.Empty, actions, title);
    }

    private static bool HasRecoverableAudio(Meeting meeting)
    {
        var directory = MeetingStore.MeetingDir(meeting.Id);
        foreach (var name in new[] { "mic.wav", "sys.wav" })
        {
            var path = Path.Combine(directory, name);
            if (!EncryptedAudioFile.IsEncrypted(path)) continue;
            try
            {
                using var reader = new EncryptedAudioReader(path);
                if (reader.PcmLength == 0) continue;
                using var audio = reader.OpenWavStream();
                var buffer = new byte[64 * 1024];
                while (audio.Read(buffer, 0, buffer.Length) > 0) { }
                return true;
            }
            catch (Exception ex) when (ex is IOException or CryptographicException) { }
        }
        return false;
    }
}
