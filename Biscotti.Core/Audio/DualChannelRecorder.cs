using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Biscotti.Core.Audio;

/// <summary>
/// Dual-capture recorder: your microphone as "You", and Windows loopback of
/// the default output device ("Speakers") as everyone else. 16-bit mono 16kHz
/// streams, each written to its own WAV so transcription/merging can label
/// speakers per channel. No bot joins the call — this just records what your
/// machine plays and hears.
/// </summary>
public sealed class DualChannelRecorder : IDisposable
{
    private WaveInEvent? _mic;
    private WasapiLoopbackCapture? _loopback;
    private WavStreamWriter? _micWav;
    private WavStreamWriter? _sysWav;

    public string? MicWavPath { get; private set; }
    public string? SysWavPath { get; private set; }

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

        // --- Microphone capture. Windows' shared-mode audio engine resamples
        // --- to whatever we ask for, so 16k mono works on any device.
        _mic = new WaveInEvent
        {
            DeviceNumber = preferredMicDevice ?? 0,
            WaveFormat = new WaveFormat(AudioConfig.SampleRate, AudioConfig.BitsPerSample, AudioConfig.Channels),
            BufferMilliseconds = 50,
        };
        _mic.DataAvailable += (_, e) => _micWav!.Append(e.Buffer.AsMemory(0, e.BytesRecorded));
        _mic.StartRecording();
        // --- System audio loopback, downmix to mono 16k ---
        var loopDevice = GetLoopDevice(preferredSpeakerId);
        _loopback = new WasapiLoopbackCapture(loopDevice);
        _loopback.DataAvailable += (_, e) =>
        {
            var mono16k = LoopbackDownmixer.ToMono16k(e.Buffer, e.BytesRecorded, _loopback.WaveFormat);
            _sysWav!.Append(mono16k);
        };
        _loopback.StartRecording();
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

/// <summary>
/// Converts WASAPI loopback's float32 stereo (typically 48k) to 16-bit mono 16k,
/// averaging channels. Matches Whisper.net's expected input format exactly.
/// </summary>
public static class LoopbackDownmixer
{
    public static byte[] ToMono16k(byte[] wasapi, int count, WaveFormat fmt)
    {
        // WASAPI loopback delivers IEEE float, interleaved channels
        int bytesPerSample = 4;
        int channels = fmt.Channels;
        int framesIn = count / (bytesPerSample * channels);
        var outSamples = new List<short>(framesIn);

        // naive linear decimation from fmt.SampleRate to 16k. Acceptable for speech.
        double ratio = (double)fmt.SampleRate / AudioConfig.SampleRate;
        for (int outIdx = 0; outIdx * ratio < framesIn; outIdx++)
        {
            int inIdx = (int)(outIdx * ratio);
            double acc = 0;
            for (int ch = 0; ch < channels; ch++)
            {
                int off = inIdx * channels * bytesPerSample + ch * bytesPerSample;
                acc += BitConverter.ToSingle(wasapi, off);
            }
            float v = (float)(acc / channels);
            v = Math.Clamp(v, -1f, 1f);
            outSamples.Add((short)(v * short.MaxValue));
        }
        var bytes = new byte[outSamples.Count * 2];
        Buffer.BlockCopy(outSamples.ToArray(), 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
