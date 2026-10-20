using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Biscotti.Core.Audio;

public sealed record OutputDeviceChoice(string? Id, string Name);
public sealed record AudioDeviceSnapshot(
    IReadOnlyList<WaveInDeviceInfo> Microphones,
    IReadOnlyList<OutputDeviceChoice> Outputs,
    string? DefaultOutputId);

public sealed class AudioDeviceException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Device descriptions own no native handles and can safely be retained by the UI.</summary>
public static class AudioDevices
{
    public static AudioDeviceSnapshot Enumerate()
    {
        try { return ReadDevices(); }
        catch (Exception ex)
        {
            throw new AudioDeviceException("Audio devices could not be listed. Check their connections and choose devices again. " + ex.Message, ex);
        }
    }

    private static AudioDeviceSnapshot ReadDevices()
    {
        var microphones = new List<WaveInDeviceInfo>();
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            microphones.Add(new(i, WaveInEvent.GetCapabilities(i).ProductName));

        var outputs = new List<OutputDeviceChoice> { new(null, "Windows default output") };
        using var enumerator = new MMDeviceEnumerator();
        var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var endpoint in endpoints)
        {
            using (endpoint) outputs.Add(new(endpoint.ID, endpoint.FriendlyName));
        }
        string? defaultId = null;
        if (enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Console))
        {
            using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            defaultId = endpoint.ID;
        }
        return new(microphones, outputs, defaultId);
    }

    public static void Validate(AppSettings settings, AudioDeviceSnapshot devices)
    {
        if (!devices.Microphones.Any(m => m.Index == settings.MicDeviceIndex && m.Name == settings.MicDeviceName))
            throw new AudioDeviceException("Choose an available microphone in Audio devices before recording.");
        if (settings.SpeakerDeviceId is null ? devices.DefaultOutputId is null
            : !devices.Outputs.Any(o => o.Id == settings.SpeakerDeviceId))
            throw new AudioDeviceException("The selected output is unavailable. Choose an available output in Audio devices.");
    }
}

/// <summary>Compares device snapshots without needing audio hardware in regression checks.</summary>
internal sealed class AudioDeviceChangeTracker(AppSettings settings, AudioDeviceSnapshot initial)
{
    private readonly int? _micIndex = settings.MicDeviceIndex;
    private readonly string? _micName = settings.MicDeviceName;
    private readonly bool _usesDefault = settings.SpeakerDeviceId is null;
    private readonly string? _outputId = settings.SpeakerDeviceId ?? initial.DefaultOutputId;
    private readonly string? _initialDefault = initial.DefaultOutputId;
    private readonly HashSet<string> _reported = [];

    public IEnumerable<string> Observe(AudioDeviceSnapshot devices)
    {
        if (!devices.Microphones.Any(m => m.Index == _micIndex && m.Name == _micName))
        {
            const string message = "The selected microphone changed or disconnected. Stop and choose devices before starting a new recording.";
            if (_reported.Add(message)) yield return message;
        }
        if (!devices.Outputs.Any(o => o.Id == _outputId && o.Id is not null))
        {
            const string message = "The recording output disconnected. Stop and choose devices before starting a new recording.";
            if (_reported.Add(message)) yield return message;
        }
        if (_usesDefault && devices.DefaultOutputId != _initialDefault)
        {
            const string message = "Windows default output changed. This recording stays on the original output. Stop and choose devices to capture the new output.";
            if (_reported.Add(message)) yield return message;
        }
    }
}

/// <summary>One disposable polling subscription per recording; silent loopback still gets monitored.</summary>
internal sealed class AudioDeviceWatch : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<AudioDeviceSnapshot> _enumerate;
    private readonly AudioDeviceChangeTracker _tracker;
    private readonly Action<string> _warning;
    private readonly Timer _timer;
    private bool _disposed;

    internal AudioDeviceWatch(AppSettings settings, AudioDeviceSnapshot initial, Action<string> warning,
        Func<AudioDeviceSnapshot>? enumerate = null, TimeSpan? interval = null)
    {
        _enumerate = enumerate ?? AudioDevices.Enumerate;
        _tracker = new(settings, initial);
        _warning = warning;
        var period = interval ?? TimeSpan.FromSeconds(1);
        _timer = new Timer(_ => Poll(), null, period, period);
    }

    internal void Poll()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                foreach (var warning in _tracker.Observe(_enumerate())) _warning(warning);
            }
            catch (Exception)
            {
                _warning("Audio device availability could not be checked. Check Windows sound settings before continuing.");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
        }
    }
}

internal static class CaptureStartup
{
    internal static void Start(Action startMicrophone, Action startOutput, Func<bool> microphoneReady, Action rollback)
    {
        try
        {
            startMicrophone();
            startOutput();
            if (!microphoneReady())
                throw new AudioDeviceException("The microphone did not provide audio. Check its connection and permissions, then choose devices again.");
        }
        catch
        {
            rollback();
            throw;
        }
    }
}
