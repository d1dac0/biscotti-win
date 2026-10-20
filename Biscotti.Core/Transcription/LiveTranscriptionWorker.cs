using Biscotti.Core.Models;
using Biscotti.Core.Audio;
using System.Security.Cryptography;

namespace Biscotti.Core.Transcription;

/// <summary>
/// Reads completed, non-overlapping PCM windows from the recorder's growing
/// encrypted containers and transcribes them serially in memory.
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
    private readonly Func<Stream, string, CancellationToken, double, Task<List<TranscriptLine>>> _transcribe;
    private Task? _workerTask;
    private volatile bool _completing;

    public event Action<IReadOnlyList<TranscriptLine>>? LinesReady;
    public event Action<string>? WarningReady;

    public LiveTranscriptionWorker(
        string micWavPath,
        string systemWavPath,
        Func<(long mic, long sys)> bytesWritten,
        string modelPath,
        string? localVocabulary = null)
    {
        _mic = new Channel(micWavPath, "You", allowLegacyPlaintext: false);
        _system = new Channel(systemWavPath, "Speaker", allowLegacyPlaintext: false);
        _bytesWritten = bytesWritten;
        _transcriber = new WhisperTranscriber(modelPath, localVocabulary);
        _transcribe = _transcriber.TranscribeWavAsync;
    }

    internal LiveTranscriptionWorker(
        string micWavPath,
        string systemWavPath,
        Func<(long mic, long sys)> bytesWritten,
        Func<Stream, string, CancellationToken, double, Task<List<TranscriptLine>>> transcribe,
        IMeetingKeyProtector? protector = null)
    {
        _mic = new Channel(micWavPath, "You", protector, allowLegacyPlaintext: true);
        _system = new Channel(systemWavPath, "Speaker", protector, allowLegacyPlaintext: true);
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

            if (_completing)
            {
                var micReadable = _mic.GetReadableLength(totals.mic);
                var systemReadable = _system.GetReadableLength(totals.sys);
                if (_mic.OffsetBytes >= micReadable && _system.OffsetBytes >= systemReadable) return;
            }

            if (!didWork)
                await Task.Delay(PollInterval);
        }
    }

    private async Task<bool> ProcessAvailableAsync(Channel channel, long totalBytes)
    {
        if (totalBytes <= channel.OffsetBytes) return false;
        var sourceLength = channel.GetReadableLength(totalBytes);
        if (_completing && channel.Reader?.HasTruncatedTail == true && !channel.TruncationReported)
        {
            channel.TruncationReported = true;
            WarningReady?.Invoke($"The final incomplete audio frame in {channel.Speaker} audio was discarded. Earlier authenticated audio remains available.");
        }
        var available = sourceLength - channel.OffsetBytes;
        if (available <= 0 || (!_completing && available < WindowBytes)) return false;

        var byteCount = (int)Math.Min(available, WindowBytes);
        var offsetSeconds = (double)channel.OffsetBytes / BytesPerSecond;
        using var wavStream = channel.OpenWavWindow(channel.OffsetBytes, byteCount);
        var lines = await _transcribe(wavStream, channel.Speaker, CancellationToken.None, offsetSeconds);
        if (lines.Count > 0) LinesReady?.Invoke(lines);

        channel.OffsetBytes += byteCount;
        return true;
    }

    public void Dispose()
    {
        _mic.Dispose();
        _system.Dispose();
        _transcriber?.Dispose();
    }

    private sealed class Channel : IDisposable
    {
        public string Path { get; }
        public string Speaker { get; }
        public long OffsetBytes { get; set; }
        public bool TruncationReported { get; set; }
        public EncryptedAudioReader? Reader { get; }

        public Channel(string path, string speaker, IMeetingKeyProtector? protector = null, bool allowLegacyPlaintext = false)
        {
            Path = path;
            Speaker = speaker;
            if (EncryptedAudioFile.IsEncrypted(path))
            {
                try { Reader = new EncryptedAudioReader(path, protector); }
                catch (CryptographicException ex) { throw MeetingAudioMigration.KeyAccessFailure(path, ex); }
                catch (InvalidDataException ex) { throw new InvalidDataException($"Could not read {MeetingAudioMigration.AudioIdentity(path)}: {ex.Message}", ex); }
            }
            else if (File.Exists(path) && !allowLegacyPlaintext)
            {
                throw new InvalidDataException($"{MeetingAudioMigration.AudioIdentity(path)} does not have an encrypted audio header.");
            }
        }

        public long GetReadableLength(long fallbackLength)
        {
            if (fallbackLength <= 0) return 0;
            if (Reader is null) return Math.Min(fallbackLength, Math.Max(0, new FileInfo(Path).Length - 44));
            Reader.Refresh();
            return Math.Min(fallbackLength, Reader.PcmLength);
        }

        public Stream OpenWavWindow(long offset, int count)
        {
            if (Reader is not null)
            {
                try { return Reader.CreateWavStream(offset, count); }
                catch (CryptographicException ex) { throw new CryptographicException($"Audio authentication failed for {MeetingAudioMigration.AudioIdentity(Path)}.", ex); }
                catch (InvalidDataException ex) { throw new InvalidDataException($"Could not read {MeetingAudioMigration.AudioIdentity(Path)}: {ex.Message}", ex); }
            }
            var pcm = new byte[count];
            using (var source = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                source.Position = 44 + offset;
                source.ReadExactly(pcm);
            }
            var wav = new MemoryStream(44 + count);
            using (var writer = new WavStreamWriter(wav, leaveOpen: true)) { writer.Append(pcm); writer.Close(); }
            return wav;
        }

        public void Dispose() => Reader?.Dispose();
    }
}
