using Biscotti.Core.Models;
using Biscotti.Core.Audio;

namespace Biscotti.Core.Transcription;

/// <summary>
/// Reads completed, non-overlapping PCM windows from the recorder's growing
/// WAV files and transcribes them serially. The source recordings remain the
/// durable audio; temporary window WAVs are deleted after each pass.
/// </summary>
internal sealed class LiveTranscriptionWorker : IDisposable
{
    private const int BytesPerSecond = AudioConfig.SampleRate * AudioConfig.Channels * (AudioConfig.BitsPerSample / 8);
    private const int WindowSeconds = 30;
    private const int WindowBytes = BytesPerSecond * WindowSeconds;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);

    private readonly Channel _mic;
    private readonly Channel _system;
    private readonly Func<(long mic, long sys)> _bytesWritten;
    private readonly WhisperTranscriber? _transcriber;
    private readonly Func<string, string, CancellationToken, double, Task<List<TranscriptLine>>> _transcribe;
    private Task? _workerTask;
    private volatile bool _completing;

    public event Action<IReadOnlyList<TranscriptLine>>? LinesReady;

    public LiveTranscriptionWorker(
        string micWavPath,
        string systemWavPath,
        Func<(long mic, long sys)> bytesWritten,
        string modelPath)
    {
        _mic = new Channel(micWavPath, "You");
        _system = new Channel(systemWavPath, "Speaker");
        _bytesWritten = bytesWritten;
        _transcriber = new WhisperTranscriber(modelPath);
        _transcribe = _transcriber.TranscribeWavAsync;
    }

    internal LiveTranscriptionWorker(
        string micWavPath,
        string systemWavPath,
        Func<(long mic, long sys)> bytesWritten,
        Func<string, string, CancellationToken, double, Task<List<TranscriptLine>>> transcribe)
    {
        _mic = new Channel(micWavPath, "You");
        _system = new Channel(systemWavPath, "Speaker");
        _bytesWritten = bytesWritten;
        _transcribe = transcribe;
    }

    public void Start()
    {
        if (_workerTask is not null) throw new InvalidOperationException("Live transcription has already started.");
        _workerTask = Task.Run(ProcessAsync);
    }

    /// <summary>Stop accepting new audio and drain every complete and partial window.</summary>
    public async Task CompleteAsync()
    {
        if (_workerTask is null) return;
        _completing = true;
        await _workerTask;
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            var totals = _bytesWritten();
            var didWork = await ProcessAvailableAsync(_mic, totals.mic);
            didWork |= await ProcessAvailableAsync(_system, totals.sys);

            if (_completing && _mic.OffsetBytes >= totals.mic && _system.OffsetBytes >= totals.sys)
                return;

            if (!didWork)
                await Task.Delay(PollInterval);
        }
    }

    private async Task<bool> ProcessAvailableAsync(Channel channel, long totalBytes)
    {
        var available = totalBytes - channel.OffsetBytes;
        if (available <= 0 || (!_completing && available < WindowBytes)) return false;

        var byteCount = (int)Math.Min(available, WindowBytes);
        var pcm = ReadPcmRange(channel.Path, channel.OffsetBytes, byteCount);
        var offsetSeconds = (double)channel.OffsetBytes / BytesPerSecond;
        var segmentPath = Path.Combine(
            Path.GetDirectoryName(channel.Path)!,
            $".{Path.GetFileNameWithoutExtension(channel.Path)}-live-{Guid.NewGuid():N}.wav");

        try
        {
            var writer = new WavStreamWriter(segmentPath);
            try
            {
                writer.Append(pcm);
                writer.Close();
            }
            catch
            {
                writer.Dispose();
                throw;
            }

            var lines = await _transcribe(segmentPath, channel.Speaker, CancellationToken.None, offsetSeconds);
            if (lines.Count > 0) LinesReady?.Invoke(lines);
        }
        finally
        {
            try { File.Delete(segmentPath); } catch { }
        }

        channel.OffsetBytes += byteCount;
        return true;
    }

    private static byte[] ReadPcmRange(string wavPath, long offsetBytes, int byteCount)
    {
        var pcm = new byte[byteCount];
        using var stream = new FileStream(wavPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(44 + offsetBytes, SeekOrigin.Begin);
        stream.ReadExactly(pcm);
        return pcm;
    }

    public void Dispose() => _transcriber?.Dispose();

    private sealed class Channel(string path, string speaker)
    {
        public string Path { get; } = path;
        public string Speaker { get; } = speaker;
        public long OffsetBytes { get; set; }
    }
}
