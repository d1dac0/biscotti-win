using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Biscotti.Core.Audio;

/// <summary>
/// Dual-capture recorder: your microphone as "You", and Windows loopback of
/// the default output device ("Speakers") as everyone else. 16-bit mono 16kHz
/// streams, each written to its own WAV so transcription/merging can label
/// speakers per channel. No bot joins the call — this just records what your
/// machine plays and hears.
///
/// Channel counts and bytes written are exposed through <see cref="TotalBytes"/>
/// so the UI can prove recording is actually alive (issue #21).
/// </summary>
public sealed class DualChannelRecorder : IDisposable
{
    private WaveInEvent? _mic;
    private WasapiLoopbackCapture? _loopback;
    private WavStreamWriter? _micWav;
    private WavStreamWriter? _sysWav;
    private AntiAliasDecimator? _micDecimator;
    private AntiAliasDecimator? _sysDecimator;
    private int _micChannels;
    private int _sysChannels;
    private long _micBytes;
    private long _sysBytes;

    public string? MicWavPath { get; private set; }
    public string? SysWavPath { get; private set; }

    /// <summary>Bytes written per channel since Start — zero means that channel is dead.</summary>
    public (long mic, long sys) TotalBytes => (_micBytes, _sysBytes);

    /// <summary>
    /// Why the mic channel is missing audio, or null when it opened fine.
    /// A dead mic must not take the system channel down with it (issue #13).
    /// </summary>
    public string? MicError { get; private set; }

    public static (List<WaveInDeviceInfo> mics, List<MMDevice> speakers) ListDevices()
    {
        var mics = new List<WaveInDeviceInfo>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            mics.Add(new WaveInDeviceInfo(i, caps.ProductName));
        }
        var speakers = new List<MMDevice>();
        using var enums = new MMDeviceEnumerator();
        foreach (var dev in enums.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            speakers.Add(dev);
        return (mics, speakers);
    }

    public void Start(string micWavPath, string sysWavPath, int? preferredMicDevice = null, string? preferredSpeakerId = null)
    {
        MicWavPath = micWavPath;
        SysWavPath = sysWavPath;
        _micWav = new WavStreamWriter(micWavPath);
        _sysWav = new WavStreamWriter(sysWavPath);

        StartMic(preferredMicDevice ?? 0);
        StartLoopback(preferredSpeakerId);
    }

    private void StartMic(int deviceNumber)
    {
        // Preferred: ask the shared-mode audio engine for 16k mono directly.
        // Some devices (Bluetooth HFP, some USB mics) reject the request
        // (issue #13): fall back to a 48k capture and convert ourselves.
        _mic = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(AudioConfig.SampleRate, AudioConfig.BitsPerSample, AudioConfig.Channels),
            BufferMilliseconds = 50,
        };
        _mic.DataAvailable += NativeMicDataAvailable;
        try
        {
            _mic.StartRecording();
            return;
        }
        catch (Exception ex)
        {
            MicError = $"16 kHz capture refused: {ex.Message}";
        }
        _mic.DataAvailable -= NativeMicDataAvailable;
        _mic.Dispose();
        _mic = null;

        _mic = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(48000, AudioConfig.BitsPerSample, AudioConfig.Channels),
            BufferMilliseconds = 50,
        };
        _micDecimator = new AntiAliasDecimator(48000);
        _micChannels = _mic.WaveFormat.Channels;
        _mic.DataAvailable += ConvertedMicDataAvailable;
        try
        {
            _mic.StartRecording();
        }
        catch (Exception ex)
        {
            // Both rates refused (device gone, exclusive-mode hold, etc):
            // keep the system channel recording and report the dead mic
            // instead of aborting the whole meeting.
            MicError = $"48 kHz fallback failed too: {ex.Message}";
            _mic.DataAvailable -= ConvertedMicDataAvailable;
            _mic.Dispose();
            _mic = null;
            _micDecimator = null;
        }
    }

    private void NativeMicDataAvailable(object? sender, WaveInEventArgs e)
    {
        // Shared-mode resampling already matches the whisper input format.
        _micWav!.Append(e.Buffer.AsMemory(0, e.BytesRecorded));
        _micBytes += e.BytesRecorded;
    }

    private void ConvertedMicDataAvailable(object? sender, WaveInEventArgs e)
    {
        var mono16k = _micDecimator!.ProcessInt16(e.Buffer, e.BytesRecorded, _micChannels);
        _micWav!.Append(mono16k);
        _micBytes += mono16k.Length;
    }

    private void StartLoopback(string? preferredSpeakerId)
    {
        var loopDevice = GetLoopDevice(preferredSpeakerId);
        _loopback = new WasapiLoopbackCapture(loopDevice);
        _sysDecimator = new AntiAliasDecimator(_loopback.WaveFormat.SampleRate);
        _sysChannels = _loopback.WaveFormat.Channels;
        _loopback.DataAvailable += LoopbackDataAvailable;
        _loopback.StartRecording();
    }

    private void LoopbackDataAvailable(object? sender, WaveInEventArgs e)
    {
        var mono16k = _sysDecimator!.ProcessFloat(e.Buffer, e.BytesRecorded, _loopback!.WaveFormat.Channels);
        _sysWav!.Append(mono16k);
        _sysBytes += mono16k.Length;
    }

    private static MMDevice GetLoopDevice(string? preferredId)
    {
        using var enums = new MMDeviceEnumerator();
        if (!string.IsNullOrEmpty(preferredId))
        {
            var dev = enums.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                       .FirstOrDefault(d => d.ID == preferredId);
            if (dev != null) return dev;
        }
        return enums.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
    }

    public (string mic, string sys) Stop()
    {
        try { _mic?.StopRecording(); } catch { }
        try { _loopback?.StopRecording(); } catch { }
        // loopback needs a beat to flush its last packet
        Thread.Sleep(200);
        _micWav?.Close();
        _sysWav?.Close();
        return (MicWavPath ?? "", SysWavPath ?? "");
    }

    public void Dispose()
    {
        _mic?.Dispose();
        _loopback?.Dispose();
        _micWav?.Dispose();
        _sysWav?.Dispose();
    }
}

public record WaveInDeviceInfo(int Index, string Name);