using Biscotti.Core.Ai;
using Biscotti.Core.Audio;
using Biscotti.Core.Calendar;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;
using Biscotti.Core.Transcription;
using System.Security.Cryptography;

namespace Biscotti.Core;

/// <summary>
/// Orchestrates one meeting end-to-end:
/// Record (mic + system loopback) -> Transcribe (Whisper, per channel) ->
/// Merge (speaker labeling) -> Summarize (Ollama / Gemma) -> Save.
/// State is reported via <see cref="StateChanged"/> so the UI can show a
/// live status without owning any of this logic.
/// </summary>
public sealed class MeetingService : IAsyncDisposable
{
    private static readonly TimeSpan AutoStopSilenceTimeout = TimeSpan.FromMinutes(5);
    private readonly DualChannelRecorder _recorder = new();
    private readonly LoopbackSilenceMonitor _loopbackSilence = new(AutoStopSilenceTimeout);
    private readonly AppSettings _settings;
    private readonly SavedMeetingProcessor _savedMeetingProcessor;
    private readonly object _meetingMutationLock = new();
    private Meeting? _current;
    private LiveTranscriptionWorker? _liveTranscription;
    private Timer? _autoStopTimer;
    private int _stopProcessStarted;
    private int _savedProcessingStarted;

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public event Action<RecordingState, string>? StateChanged;
    public event Action<Meeting>? CurrentMeetingUpdated;
    public event Action<string>? AudioWarning;

    public MeetingService(AppSettings settings)
    {
        _settings = settings;
        _savedMeetingProcessor = new SavedMeetingProcessor(settings);
        _recorder.LoopbackSamplesAvailable += OnLoopbackSamplesAvailable;
    }

    private void SetState(RecordingState s, string message)
    {
        lock (_meetingMutationLock) State = s;
        StateChanged?.Invoke(s, message);
    }

    public Meeting? Current => _current;

    /// <summary>Live per-channel byte counts for the UI's recording-health readout (issue #21).</summary>
    public (long mic, long sys) RecordingStats => _recorder.TotalBytes;
    public string SystemAudioStatus => _recorder.SystemAudioStatus;
    public string RecordingDeviceWarning => _recorder.DeviceWarning;
    public string AutoStopStatus => _loopbackSilence.IsArmed
        ? "Auto-stop armed: recording stops after 5 minutes without output audio."
        : "Auto-stop inactive until output audio is detected; in-person recording stays manual.";

    public async Task StartRecordingAsync(int? targetProcessId = null, string? targetProcessName = null,
        CalendarMeeting? calendarMeeting = null, bool allowSystemWideLoopback = false)
    {
        if (State != RecordingState.Idle) throw new InvalidOperationException("A meeting is already in progress.");
        _loopbackSilence.Reset();
        SetState(RecordingState.Starting, "Checking audio devices…");
        var previousMeeting = _current;
        Meeting? pendingMeeting = null;
        try
        {
            await Task.Run(() =>
            {
                AudioDevices.Validate(_settings, AudioDevices.Enumerate());
                var (meeting, dir) = MeetingStore.CreateNew();
                pendingMeeting = meeting;
                meeting.StartedUtc = DateTime.UtcNow;
                if (calendarMeeting is not null)
                {
                    meeting.Title = calendarMeeting.Title;
                    meeting.CalendarAttendees = calendarMeeting.Attendees.ToList();
                }
                meeting.ProcessingStatus = MeetingProcessingStatus.Recording;
                MeetingStore.Save(meeting);
                _recorder.Start(Path.Combine(dir, "mic.wav"), Path.Combine(dir, "sys.wav"),
                    _settings.MicDeviceIndex, _settings.SpeakerDeviceId, _settings.MicDeviceName,
                    targetProcessId, targetProcessName, allowSystemWideLoopback: allowSystemWideLoopback);
                _liveTranscription = new LiveTranscriptionWorker(
                    Path.Combine(dir, "mic.wav"), Path.Combine(dir, "sys.wav"),
                    () => _recorder.TotalBytes, _settings.GetResolvedModelPath(), _settings.LocalVocabulary);
            });
            _current = pendingMeeting;
            _liveTranscription!.LinesReady += AppendLiveLines;
            _liveTranscription.WarningReady += warning => AudioWarning?.Invoke(warning);
            _liveTranscription.Start();
            SetState(RecordingState.Recording, $"Recording… click Stop when the call ends. {_recorder.SystemAudioStatus}");
            _autoStopTimer = new Timer(CheckForLoopbackSilence, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        catch
        {
            try
            {
                _recorder.Stop();
                _liveTranscription?.Dispose();
                _liveTranscription = null;
                if (pendingMeeting is not null) MeetingStore.Delete(pendingMeeting.Id);
            }
            finally
            {
                _current = previousMeeting;
                SetState(RecordingState.Idle, "Recording did not start. Check Audio devices and try again.");
            }
            throw;
        }
    }

    private void AppendLiveLines(IReadOnlyList<TranscriptLine> lines)
    {
        if (lines.Count == 0) return;
        Meeting? meeting;
        lock (_meetingMutationLock)
        {
            meeting = _current;
            if (meeting is null) return;
            meeting.Transcript.AddRange(lines);
            MeetingStore.Save(meeting);
        }
        CurrentMeetingUpdated?.Invoke(meeting);
    }

    public void AddTimestampedNote(string text, double startSeconds)
    {
        var trimmedText = text.Trim();
        if (trimmedText.Length == 0) return;

        Meeting meeting;
        lock (_meetingMutationLock)
        {
            if (State != RecordingState.Recording || _current is null)
                throw new InvalidOperationException("Timestamped notes can only be added while recording.");

            meeting = _current;
            meeting.TimestampedNotes.Add(new TimestampedMeetingNote
            {
                Text = trimmedText,
                StartSeconds = Math.Max(0, startSeconds),
            });
            MeetingStore.Save(meeting);
        }
        CurrentMeetingUpdated?.Invoke(meeting);
    }

    public async Task StopAndProcessAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _stopProcessStarted, 1, 0) != 0) return;
        if (State != RecordingState.Recording || _current is null)
        {
            Interlocked.Exchange(ref _stopProcessStarted, 0);
            throw new InvalidOperationException("Not recording.");
        }

        StopAutoStopTimer();
        try { await ProcessRecordingAsync(ct); }
        catch
        {
            if (_current is not null)
            {
                _current.ProcessingStatus = _current.ProcessingStatus == MeetingProcessingStatus.Summarizing
                    ? MeetingProcessingStatus.SummaryFailed : MeetingProcessingStatus.TranscriptionFailed;
                try { MeetingStore.Save(_current); } catch { }
            }
            throw;
        }
        finally
        {
            if (State != RecordingState.Idle)
                SetState(RecordingState.Idle, "Processing stopped. The recorded audio remains saved locally.");
            Interlocked.Exchange(ref _stopProcessStarted, 0);
        }
    }

    public Task RecoverInterruptedMeetingAsync(Meeting meeting, CancellationToken ct = default) =>
        RunSavedMeetingActionAsync(meeting, RecordingState.Transcribing, "Checking saved audio…",
            () => _savedMeetingProcessor.RecoverInterruptedAsync(meeting, ct));

    public Task RetrySavedTranscriptionAsync(Meeting meeting, CancellationToken ct = default) =>
        RunSavedMeetingActionAsync(meeting, RecordingState.Transcribing, "Retrying transcription from saved audio locally…",
            () => _savedMeetingProcessor.RetryTranscriptionAsync(meeting, ct));

    public Task RegenerateSavedSummaryAsync(Meeting meeting, CancellationToken ct = default) =>
        RunSavedMeetingActionAsync(meeting, RecordingState.Summarizing, "Regenerating the summary with local AI…",
            () => _savedMeetingProcessor.RegenerateSummaryAsync(meeting, ct));

    private async Task RunSavedMeetingActionAsync(Meeting meeting, RecordingState state, string message, Func<Task> action)
    {
        if (State != RecordingState.Idle || Interlocked.CompareExchange(ref _savedProcessingStarted, 1, 0) != 0)
            throw new InvalidOperationException("Finish the current meeting before retrying saved processing.");
        try
        {
            SetState(state, message);
            await action();
            CurrentMeetingUpdated?.Invoke(meeting);
        }
        finally
        {
            Interlocked.Exchange(ref _savedProcessingStarted, 0);
            SetState(RecordingState.Idle, "Saved meeting processing finished.");
        }
    }

    private void OnLoopbackSamplesAvailable(byte[] samples, int bytesRecorded, int channels) =>
        _loopbackSilence.Observe(samples, bytesRecorded, channels, DateTimeOffset.UtcNow);

    private void CheckForLoopbackSilence(object? state)
    {
        if (State != RecordingState.Recording || !_loopbackSilence.HasTimedOut(DateTimeOffset.UtcNow)) return;
        StopAutoStopTimer();
        _ = Task.Run(async () =>
        {
            try { await StopAndProcessAsync(); }
            catch (Exception ex) { AudioWarning?.Invoke("Auto-stop could not finish processing the meeting. " + ex.Message); }
        });
    }

    private void StopAutoStopTimer() => Interlocked.Exchange(ref _autoStopTimer, null)?.Dispose();

    private async Task ProcessRecordingAsync(CancellationToken ct)
    {
        var (micPath, sysPath) = _recorder.Stop();
        _current!.EndedUtc = DateTime.UtcNow;
        _current.ProcessingStatus = MeetingProcessingStatus.Transcribing;
        MeetingStore.Save(_current);

        SetState(RecordingState.Transcribing, "Finishing the live transcript…");
        var streamSucceeded = false;
        try
        {
            if (_liveTranscription is not null)
            {
                await _liveTranscription.CompleteAsync();
                streamSucceeded = true;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CryptographicException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (IOException)
        {
            throw;
        }
        catch
        {
            SetState(RecordingState.Transcribing, "Live transcription failed; retrying from the saved audio…");
            // Keep live partial lines until saved-audio transcription succeeds.
        }
        finally
        {
            _liveTranscription?.Dispose();
            _liveTranscription = null;
        }

        if (streamSucceeded)
        {
            var micLines = _current.Transcript.Where(l => l.Speaker == "You").ToList();
            var sysLines = _current.Transcript.Where(l => l.Speaker != "You").ToList();
            _current.Transcript = TranscriptAssembler.Merge(micLines, sysLines);
        }
        else
        {
            // Defensive fallback if live transcription could not be started.
            using var transcriber = new WhisperTranscriber(_settings.GetResolvedModelPath(), _settings.LocalVocabulary);
            var micLines = await TranscribeSavedAudioAsync(transcriber, micPath, "You", ct);
            var sysLines = File.Exists(sysPath) && new FileInfo(sysPath).Length > 1000
                ? await TranscribeSavedAudioAsync(transcriber, sysPath, "Speaker", ct)
                : new List<TranscriptLine>();
            _current.Transcript = TranscriptAssembler.Merge(micLines, sysLines);
        }

        var systemLines = _current.Transcript.Where(line => line.Speaker != "You").ToList();
        await ApplySpeakerDiarizationAsync(sysPath, systemLines, ct);

        CurrentMeetingUpdated?.Invoke(_current);
        _current.ProcessingStatus = MeetingProcessingStatus.SummaryPending;
        MeetingStore.Save(_current);

        _current.ProcessingStatus = MeetingProcessingStatus.Summarizing;
        MeetingStore.Save(_current);
        SetState(RecordingState.Summarizing, "Writing summary with local AI (Ollama)…");
        using var ollama = new OllamaService(_settings.OllamaModel,
            new SummaryPreferences(_settings.SummaryLength, _settings.SummaryGuidance));
        var chunkProgress = new Progress<(int chunk, int total)>(p =>
            SetState(RecordingState.Summarizing,
                $"Writing summary with local AI (part {p.chunk} of {p.total})…"));
        var raw = await ollama.SummarizeMeetingAsync(_current.Transcript, chunkProgress, ct, _current.CalendarAttendees);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var (summary, actions, title, participantMatches) = SummaryParser.Parse(raw);
            _current.Summary = summary;
            _current.ActionItems = actions;
            if (string.IsNullOrWhiteSpace(_current.Title) && !string.IsNullOrWhiteSpace(title) && title.Length < 80)
                _current.Title = title;
            foreach (var (speakerLabel, attendeeName) in participantMatches)
            {
                if (!_current.CalendarAttendees.Contains(attendeeName, StringComparer.OrdinalIgnoreCase)) continue;
                foreach (var line in _current.Transcript.Where(line => line.Speaker == speakerLabel))
                    line.Speaker = attendeeName;
            }
        }

        SpeakerIntroductionResolver.Apply(_current.Transcript);

        _current.ProcessingStatus = string.IsNullOrWhiteSpace(raw)
            ? MeetingProcessingStatus.SummaryFailed : MeetingProcessingStatus.Complete;

        SetState(RecordingState.Idle, string.IsNullOrWhiteSpace(raw)
            ? "Transcript saved locally. Summary unavailable — check Ollama and the local model in Settings."
            : "Done — transcript and summary saved locally.");
        MeetingStore.Save(_current);
        CurrentMeetingUpdated?.Invoke(_current);
    }

    private static async Task<List<TranscriptLine>> TranscribeSavedAudioAsync(
        WhisperTranscriber transcriber, string path, string speaker, CancellationToken ct)
    {
        if (!EncryptedAudioFile.IsEncrypted(path))
            throw new InvalidDataException($"{MeetingAudioMigration.AudioIdentity(path)} does not have an encrypted audio header.");
        EncryptedAudioReader reader;
        try
        {
            reader = new EncryptedAudioReader(path);
        }
        catch (CryptographicException ex) { throw MeetingAudioMigration.KeyAccessFailure(path, ex); }
        return await transcriber.TranscribeWavAsync(reader.OpenWavStream(), speaker, ct);
    }

    private async Task ApplySpeakerDiarizationAsync(
        string systemAudioPath, List<TranscriptLine> systemLines, CancellationToken ct)
    {
        if (!SpeakerDiarizer.HasSuitableSegments(systemLines) || !File.Exists(systemAudioPath)) return;

        try
        {
            var downloadProgress = new Progress<(double pct, string phase)>(progress =>
                SetState(RecordingState.Transcribing, progress.phase));
            var modelPath = await SpeakerEmbeddingModelStore.EnsureDownloadedAsync(downloadProgress, ct);
            SetState(RecordingState.Transcribing, "Clustering remote speakers locally…");
            var applied = await Task.Run(() =>
            {
                using var encryptedReader = new EncryptedAudioReader(systemAudioPath);
                using var wavStream = encryptedReader.OpenWavStream();
                var embeddings = SpeakerDiarizer.ExtractEmbeddings(wavStream, systemLines, modelPath);
                return SpeakerDiarizer.ApplyEmbeddings(systemLines, embeddings);
            }, ct);

            if (!applied)
                AudioWarning?.Invoke("Speaker identification had too little clear loopback speech; using the basic speaker labels.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AudioWarning?.Invoke("Local speaker identification was unavailable; using the basic speaker labels. " + ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopAutoStopTimer();
        try { _recorder.Stop(); } catch { }
        try { await (_liveTranscription?.CompleteAsync() ?? Task.CompletedTask); } catch { }
        _liveTranscription?.Dispose();
        _liveTranscription = null;
        _recorder.Dispose();
        await Task.CompletedTask;
    }
}
