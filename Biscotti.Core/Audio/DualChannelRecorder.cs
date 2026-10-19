using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Biscotti.Core.Audio;

/// <summary>
/// Dual-capture recorder: your microphone as "You", and either a selected
/// process tree or the output device as everyone else. Both channels are
/// written as 16-bit mono 16 kHz WAV files for local transcription.
///
/// Channel counts and bytes written are exposed through <see cref="TotalBytes"/>
/// so the UI can prove recording is actually alive (issue #21).
/// </summary>
public sealed class DualChannelRecorder : IDisposable
{
    private WaveIn? _mic;
    private WasapiRecorder? _loopback;
    private WavStreamWriter? _micWav;
    private WavStreamWriter? _sysWav;
    private AntiAliasDecimator? _micDecimator;
    private AntiAliasDecimator? _sysDecimator;
    private int _micChannels;
    private long _micBytes;
    private long _sysBytes;

    public string? MicWavPath { get; private set; }
    public string? SysWavPath { get; private set; }

    /// <summary>Bytes written per channel since Start — zero means that channel is dead.</summary>
    public (long mic, long sys) TotalBytes => (Interlocked.Read(ref _micBytes), Interlocked.Read(ref _sysBytes));

    /// <summary>
    /// Why the mic channel is missing audio, or null when it opened fine.
    /// A dead mic must not take the system channel down with it (issue #13).
    /// </summary>
    public string? MicError { get; private set; }

    /// <summary>Describes whether system audio is app-specific or includes the full output device.</summary>
    public string SystemAudioStatus { get; private set; } = "";

    public static (List<WaveInDeviceInfo> mics, List<MMDevice> speakers) ListDevices()
    {
        var mics = new List<WaveInDeviceInfo>();
        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            mics.Add(new WaveInDeviceInfo(i, caps.ProductName));
        }
        var speakers = new List<MMDevice>();
        using var enums = new MMDeviceEnumerator();
        foreach (var dev in enums.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            speakers.Add(dev);
        return (mics, speakers);
    }

    public async Task StartAsync(string micWavPath, string sysWavPath, int? preferredMicDevice = null,
        string? preferredSpeakerId = null, int? targetProcessId = null, string? targetProcessName = null,
        int? osBuild = null)
    {
        MicWavPath = micWavPath;
        SysWavPath = sysWavPath;
        _micWav = new WavStreamWriter(micWavPath);
        _sysWav = new WavStreamWriter(sysWavPath);

        StartMic(preferredMicDevice ?? 0);
        await StartLoopbackAsync(preferredSpeakerId, targetProcessId, targetProcessName, osBuild ?? Environment.OSVersion.Version.Build);
    }

    private void StartMic(int deviceNumber)
    {
        // Preferred: ask the shared-mode audio engine for 16k mono directly.
        // Some devices (Bluetooth HFP, some USB mics) reject the request
        // (issue #13): fall back to a 48k capture and convert ourselves.
        _mic = new WaveIn
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

        _mic = new WaveIn
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
        Interlocked.Add(ref _micBytes, e.BytesRecorded);
    }

    private void ConvertedMicDataAvailable(object? sender, WaveInEventArgs e)
    {
        var mono16k = _micDecimator!.ProcessInt16(e.Buffer, e.BytesRecorded, _micChannels);
        _micWav!.Append(mono16k);
        Interlocked.Add(ref _micBytes, mono16k.Length);
    }

    private async Task StartLoopbackAsync(string? preferredSpeakerId, int? targetProcessId, string? targetProcessName, int osBuild)
    {
        var useProcessLoopback = LoopbackCapturePolicy.ShouldUseProcessLoopback(osBuild, targetProcessId);
        var processName = string.IsNullOrWhiteSpace(targetProcessName) ? "selected app" : targetProcessName;
        await Task.Run(() =>
        {
            _loopback = LoopbackCaptureStartup.StartWithFallback(
                useProcessLoopback,
                () => CreateProcessLoopbackRecorder(targetProcessId!.Value),
                ConfigureLoopback,
                recorder => recorder.StartRecording(),
                recorder => recorder.Dispose(),
                () => new WasapiRecorderBuilder()
                    .WithDevice(GetLoopDevice(preferredSpeakerId))
                    .WithLoopbackCapture()
                    .Build(),
                ConfigureLoopback,
                recorder => recorder.StartRecording(),
                recorder => recorder.Dispose(),
                reason => SystemAudioStatus = $"App-only capture could not start ({reason}). Capturing all speaker audio instead; mute notifications and music.");
        });

        if (useProcessLoopback && _loopback != null && string.IsNullOrEmpty(SystemAudioStatus))
            SystemAudioStatus = $"Capturing {processName} and its child processes only.";
        else if (string.IsNullOrEmpty(SystemAudioStatus) && osBuild < 22000)
            SystemAudioStatus = "Windows 10 captures all speaker audio. Mute notifications and music during the meeting.";
        else if (string.IsNullOrEmpty(SystemAudioStatus))
            SystemAudioStatus = "Capturing all speaker audio. Choose a meeting app for app-only capture.";
    }

    private static WasapiRecorder CreateProcessLoopbackRecorder(int processId)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new PlatformNotSupportedException("Process loopback requires Windows 10 build 19041 or later.");

        return new WasapiRecorderBuilder()
            .WithProcessLoopback((uint)processId, ProcessLoopbackMode.IncludeTargetProcessTree)
            .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
            .BuildAsync().GetAwaiter().GetResult();
    }

    private void ConfigureLoopback(WasapiRecorder recorder)
    {
        _sysDecimator = new AntiAliasDecimator(recorder.WaveFormat.SampleRate);
        var channels = recorder.WaveFormat.Channels;
        recorder.DataAvailable += (buffer, _, _, _) =>
        {
            var bytes = buffer.ToArray();
            var mono16k = _sysDecimator!.ProcessFloat(bytes, bytes.Length, channels);
            _sysWav!.Append(mono16k);
            Interlocked.Add(ref _sysBytes, mono16k.Length);
        };
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

internal static class LoopbackCapturePolicy
{
    public static bool ShouldUseProcessLoopback(int osBuild, int? processId) => osBuild >= 22000 && processId is > 0;
}

internal static class LoopbackCaptureStartup
{
    public static T StartWithFallback<T>(bool attemptProcess, Func<T> createProcess, Action<T> configureProcess,
        Action<T> startProcess, Action<T> disposeProcess, Func<T> createDevice, Action<T> configureDevice,
        Action<T> startDevice, Action<T> disposeDevice, Action<string> onFallback)
    {
        if (attemptProcess)
        {
            T? processRecorder = default;
            try
            {
                processRecorder = createProcess();
                configureProcess(processRecorder);
                startProcess(processRecorder);
                return processRecorder;
            }
            catch (Exception ex)
            {
                if (processRecorder is not null)
                {
                    try { disposeProcess(processRecorder); } catch { }
                }
                onFallback(ex.Message);
            }
        }

        var deviceRecorder = createDevice();
        try
        {
            configureDevice(deviceRecorder);
            startDevice(deviceRecorder);
            return deviceRecorder;
        }
        catch
        {
            disposeDevice(deviceRecorder);
            throw;
        }
    }
}
