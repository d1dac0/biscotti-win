using System.Text;

namespace Biscotti.Core.Audio;

/// <summary>Mic / system-audio capture settings.</summary>
public static class AudioConfig
{
    public const int SampleRate = 16000;
    public const int Channels = 1;
    public const short BitsPerSample = 16;
}

/// <summary>
/// Streaming 16-bit mono PCM WAV writer. Appends frames as they arrive and
/// fixes the RIFF header when closed, so a crash mid-meeting still leaves a
/// readable file up to that point (header samples will be zero-trimmed on close).
/// </summary>
public sealed class WavStreamWriter : IDisposable
{
    private readonly Stream _fs;
    private readonly BinaryWriter _bw;
    private readonly bool _leaveOpen;
    private long _dataBytes;
    private bool _closed;

    public WavStreamWriter(string path)
    {
        _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _bw = new BinaryWriter(_fs, Encoding.UTF8, leaveOpen: false);
        WriteHeaderPlaceholder();
    }

    public WavStreamWriter(Stream stream, bool leaveOpen = false)
    {
        if (!stream.CanSeek || !stream.CanWrite) throw new ArgumentException("WAV output must be seekable and writable.", nameof(stream));
        _fs = stream;
        _leaveOpen = leaveOpen;
        _bw = new BinaryWriter(_fs, Encoding.UTF8, leaveOpen: true);
        WriteHeaderPlaceholder();
    }

    private void WriteHeaderPlaceholder()
    {
        // "RIFF", size, "WAVE", "fmt ", fmtSize, PCM, mono, rate, byterate, blockalign, bits, "data", size
        _bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        _bw.Write(0u);
        _bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        _bw.Write(Encoding.ASCII.GetBytes("fmt "));
        _bw.Write(16u);
        _bw.Write((short)1); // PCM
        _bw.Write((short)AudioConfig.Channels);
        _bw.Write(AudioConfig.SampleRate);
        _bw.Write(AudioConfig.SampleRate * AudioConfig.Channels * AudioConfig.BitsPerSample / 8);
        _bw.Write((short)(AudioConfig.Channels * AudioConfig.BitsPerSample / 8));
        _bw.Write((short)AudioConfig.BitsPerSample);
        _bw.Write(Encoding.ASCII.GetBytes("data"));
        _bw.Write(0u);
    }

    public void Append(ReadOnlyMemory<byte> pcmBytes)
    {
        if (_closed) throw new InvalidOperationException("WavStreamWriter already closed.");
        _bw.Write(pcmBytes.Span);
        _dataBytes += pcmBytes.Length;
        MaintainHeaderSoCrashesLeaveReadableFile();
    }

    /// <summary>
    /// Rewrites RIFF/data sizes after every append. If we are killed mid-meeting
    /// (Windows Update, crash, power loss) the file on disk is still a coherent
    /// WAV readable up to the last written byte. Doing this per-append is two
    /// sector writes; doing it only in Close() left zeros forever (issue #12).
    /// </summary>
    private void MaintainHeaderSoCrashesLeaveReadableFile()
    {
        var riffSize = 36 + _dataBytes;
        var pos = _fs.Position;
        _fs.Seek(4, SeekOrigin.Begin);
        _bw.Write((uint)riffSize);
        _fs.Seek(40, SeekOrigin.Begin);
        _bw.Write((uint)_dataBytes);
        _bw.Flush();
        _fs.Seek(pos, SeekOrigin.Begin);
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _bw.Flush();
        var riffSize = 36 + _dataBytes;
        _fs.Seek(4, SeekOrigin.Begin);
        _bw.Write((uint)riffSize);
        _fs.Seek(40, SeekOrigin.Begin);
        _bw.Write((uint)_dataBytes);
        _bw.Flush();
        Dispose();
    }

    public void Dispose()
    {
        _bw.Dispose();
        if (!_leaveOpen) _fs.Dispose();
    }
}
