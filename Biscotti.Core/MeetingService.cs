using Biscotti.Core.Ai;
using Biscotti.Core.Audio;
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
    private readonly DualChannelRecorder _recorder = new();
    private readonly AppSettings _settings;
    private Meeting? _current;
    private LiveTranscriptionWorker? _liveTranscription;

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public event Action<RecordingState, string>? StateChanged;
    public event Action<Meeting>? CurrentMeetingUpdated;
    public event Action<string>? AudioWarning;

    public MeetingService(AppSettings settings) { _settings = settings; }

    private void SetState(RecordingState s, string message)
    {
        State = s;
        StateChanged?.Invoke(s, message);
    }

    public Meeting? Current => _current;

    /// <summary>Live per-channel byte counts for the UI's recording-health readout (issue #21).</summary>
    public (long mic, long sys) RecordingStats => _recorder.TotalBytes;
    public string RecordingDeviceWarning => _recorder.DeviceWarning;

    public async Task StartRecordingAsync()
    {
        if (State != RecordingState.Idle) throw new InvalidOperationException("A meeting is already in progress.");
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
                _recorder.Start(Path.Combine(dir, "mic.wav"), Path.Combine(dir, "sys.wav"),
                    _settings.MicDeviceIndex, _settings.SpeakerDeviceId, _settings.MicDeviceName);
                _liveTranscription = new LiveTranscriptionWorker(
                    Path.Combine(dir, "mic.wav"), Path.Combine(dir, "sys.wav"),
                    () => _recorder.TotalBytes, _settings.GetResolvedModelPath());
            });
            _current = pendingMeeting;
            _liveTranscription!.LinesReady += AppendLiveLines;
            _liveTranscription.WarningReady += warning => AudioWarning?.Invoke(warning);
            _liveTranscription.Start();
            SetState(RecordingState.Recording, "Recording… click Stop when the call ends.");
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
        if (_current is null || lines.Count == 0) return;
        _current.Transcript.AddRange(lines);
        MeetingStore.Save(_current);
        CurrentMeetingUpdated?.Invoke(_current);
    }

    public async Task StopAndProcessAsync(CancellationToken ct = default)
    {
        if (State != RecordingState.Recording || _current is null)
            throw new InvalidOperationException("Not recording.");

        try { await ProcessRecordingAsync(ct); }
        finally
        {
            if (State != RecordingState.Idle)
                SetState(RecordingState.Idle, "Processing stopped. The recorded audio remains saved locally.");
        }
    }

    private async Task ProcessRecordingAsync(CancellationToken ct)
    {
        var (micPath, sysPath) = _recorder.Stop();
        _current!.EndedUtc = DateTime.UtcNow;
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
            _current.Transcript.Clear();
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
            using var transcriber = new WhisperTranscriber(_settings.GetResolvedModelPath());
            var micLines = await TranscribeSavedAudioAsync(transcriber, micPath, "You", ct);
            var sysLines = File.Exists(sysPath) && new FileInfo(sysPath).Length > 1000
                ? await TranscribeSavedAudioAsync(transcriber, sysPath, "Speaker", ct)
                : new List<TranscriptLine>();
            _current.Transcript = TranscriptAssembler.Merge(micLines, sysLines);
        }

        CurrentMeetingUpdated?.Invoke(_current);
        MeetingStore.Save(_current);

        SetState(RecordingState.Summarizing, "Writing summary with local AI (Ollama)…");
        using var ollama = new OllamaService(_settings.OllamaModel);
        var chunkProgress = new Progress<(int chunk, int total)>(p =>
            SetState(RecordingState.Summarizing,
                $"Writing summary with local AI (part {p.chunk} of {p.total})…"));
        var raw = await ollama.SummarizeMeetingAsync(_current.Transcript, chunkProgress, ct);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var (summary, actions, title) = SummaryParser.Parse(raw);
            _current.Summary = summary;
            _current.ActionItems = actions;
            if (!string.IsNullOrWhiteSpace(title) && title.Length < 80) _current.Title = title;
        }

        SetState(RecordingState.Idle, "Done — transcript and summary saved locally.");
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

    public async ValueTask DisposeAsync()
    {
        try { _recorder.Stop(); } catch { }
        try { await (_liveTranscription?.CompleteAsync() ?? Task.CompletedTask); } catch { }
        _liveTranscription?.Dispose();
        _liveTranscription = null;
        _recorder.Dispose();
        await Task.CompletedTask;
    }
}
