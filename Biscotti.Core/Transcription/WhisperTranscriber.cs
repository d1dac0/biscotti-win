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
    private readonly string? _initialPrompt;
    private readonly object _initLock = new();
    private bool _initialized;

    public WhisperTranscriber(string modelPath, string? localVocabulary = null)
    {
        _modelPath = modelPath;
        _initialPrompt = BuildInitialPrompt(localVocabulary);
    }

    internal static string? BuildInitialPrompt(string? localVocabulary)
    {
        if (string.IsNullOrWhiteSpace(localVocabulary)) return null;

        var terms = localVocabulary
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return terms.Length == 0
            ? null
            : $"Names and terms that may appear in the audio: {string.Join(", ", terms)}.";
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
        await using var fileReader = new FileStream(wavPath, FileMode.Open, FileAccess.Read);
        return await TranscribeWavAsync(fileReader, speakerLabel, ct, offsetSeconds);
    }

    public async Task<List<TranscriptLine>> TranscribeWavAsync(
        Stream wavStream, string speakerLabel, CancellationToken ct = default, double offsetSeconds = 0)
    {
        await using var ownedStream = wavStream;
        var results = new List<TranscriptLine>();
        var builder = GetFactory().CreateBuilder()
            .WithLanguage("auto")
            .WithNoContext();
        if (_initialPrompt is not null)
            builder.WithPrompt(_initialPrompt);
        await using var processor = builder.Build();
        await foreach (var seg in processor.ProcessAsync(ownedStream, ct))
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
/// Initial labels before speaker embeddings run: the mic channel is "You" and
/// the system channel gets gap-based fallback labels. Final processing replaces
/// those labels with acoustic speaker clusters when embeddings are available.
/// </summary>
public static class TranscriptAssembler
{
    public static List<TranscriptLine> Merge(
        List<TranscriptLine> mic, List<TranscriptLine> system, string yourNameGuess = "You")
    {
        foreach (var l in mic) l.Speaker = yourNameGuess;

        // Until diarization completes, keep transcript rendering useful with
        // the previous gap-based labels as a fallback.
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
