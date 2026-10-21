namespace Biscotti.Core.Audio;

/// <summary>A transient recording notice; never persisted or sent to Action Center.</summary>
public sealed record AutoStopNotice(bool Enabled, bool IsArmed, bool KeptRecording,
    int? SecondsRemaining = null, bool ShouldStop = false)
{
    public string Description => !Enabled ? "Auto-stop disabled. Stop recording manually."
        : KeptRecording ? "Keeping this recording: auto-stop is off until the next recording."
        : SecondsRemaining is { } seconds ? $"No output audio for 5 minutes. Stopping in {seconds} seconds."
        : IsArmed ? "Auto-stop armed: 5 minutes without output audio starts a 30-second countdown. Quiet calls can trigger this."
        : "Auto-stop inactive until output audio is detected; microphone-only recording stays manual.";
}

/// <summary>Timestamp-driven policy. Callers serialize access and supply a virtual clock in tests.</summary>
internal sealed class RecordingAutoStop
{
    private readonly LoopbackSilenceMonitor _silence = new(TimeSpan.FromMinutes(5));
    private bool _enabled;
    private bool _recording;
    private bool _keptRecording;
    private bool _stopRequested;
    private DateTimeOffset? _deadline;

    public bool CanStop => _recording && _enabled && _stopRequested;

    public void Begin(bool enabled)
    {
        End();
        _recording = true;
        _enabled = enabled;
    }

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        _silence.Reset();
        _deadline = null;
        _keptRecording = false;
    }

    public void Observe(byte[] samples, int bytesRecorded, int channels, DateTimeOffset now)
    {
        if (!_recording || !_enabled || _keptRecording || _stopRequested) return;
        _silence.Observe(samples, bytesRecorded, channels, now);
        if (!_silence.HasTimedOut(now)) _deadline = null;
    }

    public void KeepRecording()
    {
        if (!_recording) return;
        _keptRecording = true;
        _stopRequested = false;
        _deadline = null;
    }

    public AutoStopNotice Poll(DateTimeOffset now)
    {
        if (!_recording || !_enabled || _keptRecording || _stopRequested)
            return new(_enabled, false, _keptRecording);
        if (_silence.HasTimedOut(now))
        {
            // Give the full warning interval even after a delayed timer tick/sleep.
            _deadline ??= now.AddSeconds(30);
            if (now >= _deadline)
            {
                _stopRequested = true;
                _deadline = null;
                return new(_enabled, true, false, ShouldStop: true);
            }
        }
        return new(_enabled, _silence.IsArmed, false, _deadline is { } deadline
            ? (int)Math.Ceiling((deadline - now).TotalSeconds) : null);
    }

    public void End()
    {
        _recording = false;
        _silence.Reset();
        _deadline = null;
        _keptRecording = false;
        _stopRequested = false;
    }
}
