using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Biscotti.Core.Audio;

/// <summary>Captures the chosen mic and output into separate encrypted audio containers.</summary>
public sealed class DualChannelRecorder : IDisposable
{
    private WaveInEvent? _mic;
    private WasapiLoopbackCapture? _loopback;
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
    private volatile bool _stopping = true;
    private volatile string? _startupFailure;

    public string? MicWavPath { get; private set; }
    public string? SysWavPath { get; private set; }
    public (long mic, long sys) TotalBytes => (Interlocked.Read(ref _micBytes), Interlocked.Read(ref _sysBytes));
    public string? MicError { get; private set; }
    public string DeviceWarning { get { lock (_warningGate) return string.Join("\n", _warnings); } }
    public event Action<byte[], int, int>? LoopbackSamplesAvailable;

    public void Start(string micWavPath, string sysWavPath, int? preferredMicDevice = null,
        string? preferredSpeakerId = null, string? preferredMicName = null)
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
        _startupFailure = null;
        _microphoneReady.Reset();
        MicWavPath = micWavPath;
        SysWavPath = sysWavPath;
        var meetingKey = RandomNumberGenerator.GetBytes(32);
        var micId = RandomChannelId();
        var sysId = RandomChannelId();
        while (sysId == micId) sysId = RandomChannelId();
        try
        {
            _micWav = new EncryptedAudioWriter(micWavPath, meetingKey, micId);
            _sysWav = new EncryptedAudioWriter(sysWavPath, meetingKey, sysId);
            lock (_audioGate) _acceptAudio = true;
            _stopping = false;
            CaptureStartup.Start(
                () => StartMic(preferredMicDevice!.Value),
                () => StartLoopback(preferredSpeakerId),
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

    private void StartMic(int deviceNumber)
    {
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

    private void StartLoopback(string? preferredId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _loopDevice = preferredId is null
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console)
                : enumerator.GetDevice(preferredId);
            if (_loopDevice.State != DeviceState.Active)
                throw new AudioDeviceException("The selected output is no longer active.");
            _loopback = new WasapiLoopbackCapture(_loopDevice);
            _sysDecimator = new AntiAliasDecimator(_loopback.WaveFormat.SampleRate);
            _loopback.DataAvailable += LoopbackDataAvailable;
            _loopback.RecordingStopped += CaptureStopped;
            _loopback.StartRecording();
        }
        catch (Exception ex)
        {
            throw new AudioDeviceException("The selected output could not start. Choose an available output in Audio devices. " + ex.Message, ex);
        }
    }

    private void LoopbackDataAvailable(object? sender, WaveInEventArgs e)
    {
        int channels;
        lock (_audioGate)
        {
            if (!_acceptAudio || !ReferenceEquals(sender, _loopback)) return;
            channels = _loopback!.WaveFormat.Channels;
            var mono16k = _sysDecimator!.ProcessFloat(e.Buffer, e.BytesRecorded, _loopback!.WaveFormat.Channels);
            _sysWav!.Append(mono16k);
            Interlocked.Add(ref _sysBytes, mono16k.Length);
        }
        LoopbackSamplesAvailable?.Invoke(e.Buffer, e.BytesRecorded, channels);
    }

    private void CaptureStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopping || (!ReferenceEquals(sender, _mic) && !ReferenceEquals(sender, _loopback))) return;
        var source = ReferenceEquals(sender, _mic) ? "Microphone" : "Output audio";
        var message = source + " capture stopped unexpectedly. Stop and choose devices before starting a new recording."
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
        try { _loopback?.StopRecording(); } catch { }
        // Retain the existing bounded flush interval before closing the encrypted writers.
        if (_mic is not null || _loopback is not null) Thread.Sleep(200);
        lock (_audioGate) _acceptAudio = false;
        try { _mic?.Dispose(); } catch { }
        try { _loopback?.Dispose(); } catch { }
        try { _loopDevice?.Dispose(); } catch { }
        _mic = null;
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

public record WaveInDeviceInfo(int Index, string Name);
