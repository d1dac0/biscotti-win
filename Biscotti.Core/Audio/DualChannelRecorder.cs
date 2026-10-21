using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Biscotti.Core.Audio;

/// <summary>Captures the chosen mic and either a meeting app or output into encrypted audio containers.</summary>
public sealed class DualChannelRecorder : IDisposable
{
    private WaveInEvent? _mic;
    private WasapiRecorder? _aecMic;
    private WasapiRecorder? _loopback;
    private MMDevice? _loopDevice;
    private EncryptedAudioWriter? _micWav;
    private EncryptedAudioWriter? _sysWav;
    private AntiAliasDecimator? _micDecimator;
    private AntiAliasDecimator? _sysDecimator;
    private AudioDeviceWatch? _deviceWatch;
    private readonly object _audioGate = new();
    private readonly object _warningGate = new();
    private readonly HashSet<string> _warnings = [];
    private readonly ManualResetEventSlim _microphoneReady = new();
    private int _micChannels;
    private long _micBytes;
    private long _sysBytes;
    private bool _acceptAudio;
    private string _loopbackStatus = "";
    private string _echoCancellationStatus = "";
    private volatile bool _stopping = true;
    private volatile string? _startupFailure;

    public string? MicWavPath { get; private set; }
    public string? SysWavPath { get; private set; }
    public (long mic, long sys) TotalBytes => (Interlocked.Read(ref _micBytes), Interlocked.Read(ref _sysBytes));
    public string? MicError { get; private set; }
    public string DeviceWarning { get { lock (_warningGate) return string.Join("\n", _warnings); } }
    public string SystemAudioStatus => string.Join(" · ", new[] { _loopbackStatus, _echoCancellationStatus }
        .Where(status => !string.IsNullOrWhiteSpace(status)));
    public event Action<byte[], int, int>? LoopbackSamplesAvailable;

    public void Start(string micWavPath, string sysWavPath, int? preferredMicDevice = null,
        string? preferredSpeakerId = null, string? preferredMicName = null,
        string? preferredMicEndpointId = null,
        int? targetProcessId = null, string? targetProcessName = null, int? osBuild = null,
        bool allowSystemWideLoopback = false)
    {
        var selection = new AppSettings { MicDeviceIndex = preferredMicDevice,
            MicDeviceName = preferredMicName, SpeakerDeviceId = preferredSpeakerId };
        var devices = AudioDevices.Enumerate();
        AudioDevices.Validate(selection, devices);
        ReleaseCapture();
        Interlocked.Exchange(ref _micBytes, 0);
        Interlocked.Exchange(ref _sysBytes, 0);
        lock (_warningGate) _warnings.Clear();
        MicError = null;
        _loopbackStatus = "";
        _echoCancellationStatus = "";
        _startupFailure = null;
        _microphoneReady.Reset();
        MicWavPath = micWavPath;
        SysWavPath = sysWavPath;
        var meetingKey = RandomNumberGenerator.GetBytes(32);
        var micId = RandomChannelId();
        var sysId = RandomChannelId();
        while (sysId == micId) sysId = RandomChannelId();
        var micEndpointId = preferredMicEndpointId ?? devices.Microphones.First(m =>
            m.Index == preferredMicDevice && m.Name == preferredMicName).EndpointId;
        try
        {
            _micWav = new EncryptedAudioWriter(micWavPath, meetingKey, micId);
            _sysWav = new EncryptedAudioWriter(sysWavPath, meetingKey, sysId);
            lock (_audioGate) _acceptAudio = true;
            _stopping = false;
            CaptureStartup.Start(
                () => StartMic(preferredMicDevice!.Value, micEndpointId, preferredSpeakerId,
                    osBuild ?? Environment.OSVersion.Version.Build),
                () => StartLoopback(preferredSpeakerId, targetProcessId, targetProcessName,
                    osBuild ?? Environment.OSVersion.Version.Build, allowSystemWideLoopback),
                () => _microphoneReady.Wait(TimeSpan.FromSeconds(2)) && _startupFailure is null,
                ReleaseCapture);
            if (_startupFailure is not null) throw new AudioDeviceException(_startupFailure);
            _deviceWatch = new AudioDeviceWatch(selection, devices, AddWarning);
        }
        catch
        {
            ReleaseCapture();
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(meetingKey); }
    }

    private void AddWarning(string message)
    {
        lock (_warningGate) _warnings.Add(message);
    }

    private static uint RandomChannelId()
    {
        Span<byte> id = stackalloc byte[4];
        RandomNumberGenerator.Fill(id);
        return BinaryPrimitives.ReadUInt32BigEndian(id);
    }

    private void StartMic(int deviceNumber, string? endpointId, string? preferredSpeakerId, int osBuild)
    {
        if (WindowsEchoCancellationPolicy.CanUseReferenceControl(osBuild, endpointId))
        {
            try
            {
                OpenAecMicrophone(endpointId!, preferredSpeakerId);
                _echoCancellationStatus = "Windows echo cancellation is active for the selected microphone and output.";
                return;
            }
            catch (Exception)
            {
                var failedRecorder = _aecMic;
                _aecMic = null;
                try { failedRecorder?.StopRecording(); } catch { }
                try { failedRecorder?.Dispose(); } catch { }
                _micDecimator = null;
                _startupFailure = null;
                _microphoneReady.Reset();
                _echoCancellationStatus = "Windows echo cancellation is unavailable for this microphone or output. Microphone audio is recorded without echo cancellation.";
            }
        }
        else
        {
            _echoCancellationStatus = WindowsEchoCancellationPolicy.UnavailableStatus(osBuild, endpointId);
        }

        try { OpenMicrophone(deviceNumber, AudioConfig.SampleRate); }
        catch (Exception first)
        {
            _mic?.Dispose();
            _mic = null;
            _startupFailure = null;
            _microphoneReady.Reset();
            try { OpenMicrophone(deviceNumber, 48000); }
            catch (Exception fallback)
            {
                MicError = $"Microphone refused 16 kHz ({first.Message}) and 48 kHz ({fallback.Message}).";
                throw new AudioDeviceException(MicError + " Check its connection and permissions, then choose devices again.", fallback);
            }
        }
    }

    private void OpenAecMicrophone(string endpointId, string? preferredSpeakerId)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var microphone = enumerator.GetDevice(endpointId);
        var referenceOutput = GetLoopDevice(preferredSpeakerId);
        var recorder = new WasapiRecorderBuilder()
            .WithDevice(microphone)
            .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1))
            .WithCommunicationsMode()
            .WithEchoCancellationReferenceEndpoint(referenceOutput)
            .Build();
        _micChannels = 1;
        _micDecimator = new AntiAliasDecimator(48000);
        recorder.DataAvailable += (buffer, _, _, _) =>
        {
            lock (_audioGate)
            {
                if (!_acceptAudio || !ReferenceEquals(recorder, _aecMic) || buffer.Length == 0) return;
                var pcm = _micDecimator!.ProcessFloat(buffer.ToArray(), buffer.Length, _micChannels);
                if (pcm.Length == 0) return;
                _micWav!.Append(pcm);
                Interlocked.Add(ref _micBytes, pcm.Length);
                _microphoneReady.Set();
            }
        };
        recorder.RecordingStopped += (_, _) =>
        {
            if (_stopping) return;
            const string message = "Microphone capture stopped unexpectedly. Stop and choose devices before starting a new recording.";
            _startupFailure = message;
            _microphoneReady.Set();
            AddWarning(message);
        };
        _aecMic = recorder;
        try
        {
            recorder.StartRecording();
            if (!_microphoneReady.Wait(TimeSpan.FromSeconds(2)) || _startupFailure is not null)
                throw new AudioDeviceException("Windows microphone processing did not provide audio within the startup interval.");
        }
        catch
        {
            _aecMic = null;
            try { recorder.StopRecording(); } catch { }
            recorder.Dispose();
            _micDecimator = null;
            throw;
        }
    }

    private void OpenMicrophone(int deviceNumber, int sampleRate)
    {
        _mic = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(sampleRate, AudioConfig.BitsPerSample, AudioConfig.Channels),
            BufferMilliseconds = 50,
        };
        _micChannels = _mic.WaveFormat.Channels;
        _micDecimator = sampleRate == AudioConfig.SampleRate ? null : new AntiAliasDecimator(sampleRate);
        _mic.DataAvailable += MicDataAvailable;
        _mic.RecordingStopped += CaptureStopped;
        _mic.StartRecording();
    }

    private void MicDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_audioGate)
        {
            if (!_acceptAudio || !ReferenceEquals(sender, _mic) || e.BytesRecorded == 0) return;
            var pcm = _micDecimator is null ? e.Buffer.AsMemory(0, e.BytesRecorded)
                : _micDecimator.ProcessInt16(e.Buffer, e.BytesRecorded, _micChannels).AsMemory();
            _micWav!.Append(pcm);
            Interlocked.Add(ref _micBytes, pcm.Length);
            _microphoneReady.Set();
        }
    }

    private void StartLoopback(string? preferredId, int? targetProcessId, string? targetProcessName,
        int osBuild, bool allowSystemWideLoopback)
    {
        // No process ID means the caller selected all speaker audio explicitly.
        allowSystemWideLoopback |= targetProcessId is null;
        var useProcessLoopback = LoopbackCapturePolicy.ShouldUseProcessLoopback(
            osBuild, targetProcessId, forceSystemWide: allowSystemWideLoopback);
        var processName = string.IsNullOrWhiteSpace(targetProcessName) ? "selected app" : targetProcessName;
        if (targetProcessId is > 0 && !useProcessLoopback && !allowSystemWideLoopback)
            throw new SystemWideCaptureConsentException(
                "This Windows version cannot capture only the selected app. Choose whether to record all speaker audio, including audio from other apps.");

        _loopback = LoopbackCaptureStartup.StartWithFallback(
            useProcessLoopback,
            allowSystemWideLoopback,
            () => CreateProcessLoopbackRecorder(targetProcessId!.Value),
            ConfigureLoopback,
            recorder => recorder.StartRecording(),
            recorder => recorder.Dispose(),
            () => new WasapiRecorderBuilder().WithDevice(GetLoopDevice(preferredId)).WithLoopbackCapture().Build(),
            ConfigureLoopback,
            recorder => recorder.StartRecording(),
            recorder => recorder.Dispose(),
            reason => _loopbackStatus = $"App-only capture could not start ({reason}). Capturing all speaker audio; mute notifications and music.");

        if (useProcessLoopback && string.IsNullOrEmpty(_loopbackStatus))
            _loopbackStatus = $"Capturing {processName} and its child processes only.";
        else if (string.IsNullOrEmpty(_loopbackStatus) && osBuild < 22000)
            _loopbackStatus = "Windows 10 captures all speaker audio. Mute notifications and music during the meeting.";
        else if (string.IsNullOrEmpty(_loopbackStatus))
            _loopbackStatus = "Capturing all speaker audio. Choose a meeting app for app-only capture.";
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
        _loopback = recorder;
        _sysDecimator = new AntiAliasDecimator(recorder.WaveFormat.SampleRate);
        var channels = recorder.WaveFormat.Channels;
        recorder.DataAvailable += (buffer, _, _, _) =>
        {
            var bytes = buffer.ToArray();
            lock (_audioGate)
            {
                if (!_acceptAudio || !ReferenceEquals(recorder, _loopback)) return;
                var mono16k = _sysDecimator!.ProcessFloat(bytes, bytes.Length, channels);
                _sysWav!.Append(mono16k);
                Interlocked.Add(ref _sysBytes, mono16k.Length);
            }
            LoopbackSamplesAvailable?.Invoke(bytes, bytes.Length, channels);
        };
    }

    private MMDevice GetLoopDevice(string? preferredId)
    {
        if (_loopDevice is not null) return _loopDevice;
        using var enumerator = new MMDeviceEnumerator();
        var device = string.IsNullOrEmpty(preferredId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console)
            : enumerator.GetDevice(preferredId);
        if (device.State != DeviceState.Active)
        {
            device.Dispose();
            throw new AudioDeviceException("The selected output is no longer active. Choose an available output in Audio devices.");
        }
        _loopDevice = device;
        return device;
    }

    private void CaptureStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopping || !ReferenceEquals(sender, _mic)) return;
        var message = "Microphone capture stopped unexpectedly. Stop and choose devices before starting a new recording."
            + (e.Exception is null ? "" : " " + e.Exception.Message);
        _startupFailure = message;
        _microphoneReady.Set();
        AddWarning(message);
    }

    public (string mic, string sys) Stop()
    {
        ReleaseCapture();
        return (MicWavPath ?? "", SysWavPath ?? "");
    }

    private void ReleaseCapture()
    {
        _stopping = true;
        _deviceWatch?.Dispose();
        _deviceWatch = null;
        try { _mic?.StopRecording(); } catch { }
        try { _aecMic?.StopRecording(); } catch { }
        try { _loopback?.StopRecording(); } catch { }
        // Retain the existing bounded flush interval before closing the encrypted writers.
        if (_mic is not null || _aecMic is not null || _loopback is not null) Thread.Sleep(200);
        lock (_audioGate) _acceptAudio = false;
        try { _mic?.Dispose(); } catch { }
        try { _aecMic?.Dispose(); } catch { }
        try { _loopback?.Dispose(); } catch { }
        try { _loopDevice?.Dispose(); } catch { }
        _mic = null;
        _aecMic = null;
        _loopback = null;
        _loopDevice = null;
        lock (_audioGate)
        {
            try { _micWav?.Dispose(); }
            finally { _micWav = null; try { _sysWav?.Dispose(); } finally { _sysWav = null; } }
        }
        _micDecimator = null;
        _sysDecimator = null;
    }

    public void Dispose()
    {
        ReleaseCapture();
        _microphoneReady.Dispose();
    }
}

public record WaveInDeviceInfo(int Index, string Name, string? EndpointId = null);

internal static class LoopbackCapturePolicy
{
    public static bool ShouldUseProcessLoopback(int osBuild, int? processId, bool forceSystemWide = false) =>
        !forceSystemWide && osBuild >= 22000 && processId is > 0;
}

internal static class LoopbackCaptureStartup
{
    public static T StartWithFallback<T>(bool attemptProcess, bool allowDeviceFallback,
        Func<T> createProcess, Action<T> configureProcess,
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
                if (!allowDeviceFallback)
                    throw new SystemWideCaptureConsentException(
                        $"App-only capture could not start ({ex.Message}). Choose whether to retry using all speaker audio, including audio from other apps.", ex);
                onFallback(ex.Message);
            }
        }
        else if (!allowDeviceFallback)
        {
            throw new SystemWideCaptureConsentException(
                "Choose whether to record all speaker audio, including audio from other apps.");
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

public sealed class SystemWideCaptureConsentException(string message, Exception? inner = null)
    : Exception(message, inner);
