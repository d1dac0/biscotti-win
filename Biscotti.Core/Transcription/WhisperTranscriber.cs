using Biscotti.Core.Models;
using Whisper.net;
using Whisper.net.Ggml;

namespace Biscotti.Core.Transcription;

/// <summary>
/// Local voice-to-text using Whisper.cpp via Whisper.net. Fully on-device;
/// audio bytes go from WAV file to text without any network call.
/// </summary>
public sealed class WhisperTranscriber : IDisposable
{
    private WhisperFactory? _factory;
    private readonly string _modelPath;
    private readonly object _initLock = new();
    private bool _initialized;

    public WhisperTranscriber(string modelPath)
    {
        _modelPath = modelPath;
    }

    private WhisperFactory GetFactory()
    {
        if (_initialized) return _factory!;
        lock (_initLock)
        {
            if (_initialized) return _factory!;
            _factory = WhisperFactory.FromPath(_modelPath);
            _initialized = true;
        }
        return _factory;
    }

    public async Task<List<TranscriptLine>> TranscribeWavAsync(
        string wavPath, string speakerLabel, CancellationToken ct = default, double offsetSeconds = 0)
    {
        var results = new List<TranscriptLine>();
        await using var fileReader = new FileStream(wavPath, FileMode.Open, FileAccess.Read);
        await using var processor = GetFactory().CreateBuilder()
            .WithLanguage("auto")
            .WithNoContext()
            .Build();
        await foreach (var seg in processor.ProcessAsync(fileReader, ct))
        {
            results.Add(new TranscriptLine
            {
                Speaker = speakerLabel,
                Text = seg.Text,
                StartSeconds = offsetSeconds + seg.Start.TotalSeconds,
                EndSeconds = offsetSeconds + seg.End.TotalSeconds,
            });
        }
        return results;
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _factory = null;
        _initialized = false;
    }
}

/// <summary>
/// Speaker labeling for the Windows v1: the mic channel is "You", the system
/// loopback channel is "Speaker N" (unnamed other participant). The Ollama
/// pass may refine names if people announce themselves.
/// </summary>
public static class TranscriptAssembler
{
    public static List<TranscriptLine> Merge(
        List<TranscriptLine> mic, List<TranscriptLine> system, string yourNameGuess = "You")
    {
        foreach (var l in mic) l.Speaker = yourNameGuess;

        // Walk the system channel in time order; consecutive lines with a gap
        // under 1.5s get the same "Speaker N" label since loopback doesn't
        // separate individual remote voices in v1.
        var ordered = system.OrderBy(l => l.StartSeconds).ToList();
        int speaker = 1;
        TranscriptLine? prev = null;
        foreach (var l in ordered)
        {
            if (prev != null && l.StartSeconds - prev.EndSeconds > 1.5) speaker++;
            l.Speaker = $"Speaker {speaker}";
            prev = l;
        }

        return mic.Concat(system)
            .OrderBy(l => l.StartSeconds)
            .ToList();
    }
}
