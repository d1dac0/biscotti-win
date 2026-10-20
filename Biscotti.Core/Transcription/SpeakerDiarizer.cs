using Biscotti.Core.Models;
using NAudio.Wave;
using SherpaOnnx;

namespace Biscotti.Core.Transcription;

/// <summary>Extracts voice embeddings for Whisper's timestamped loopback segments.</summary>
public static class SpeakerDiarizer
{
    private const int SampleRate = 16_000;
    private const int MinimumSamples = SampleRate;

    public static bool HasSuitableSegments(IReadOnlyList<TranscriptLine> lines) =>
        lines.Any(line => line.EndSeconds - line.StartSeconds >= 1.0);

    /// <summary>
    /// Computes one embedding per sufficiently long speech segment. Audio is
    /// expected to be a decrypted, mono 16 kHz, 16-bit PCM WAV stream.
    /// </summary>
    public static IReadOnlyList<float[]?> ExtractEmbeddings(
        Stream wavStream, IReadOnlyList<TranscriptLine> lines, string modelPath)
    {
        using var reader = new WaveFileReader(wavStream);
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm ||
            reader.WaveFormat.SampleRate != SampleRate ||
            reader.WaveFormat.Channels != 1 ||
            reader.WaveFormat.BitsPerSample != 16)
            throw new InvalidDataException("Speaker diarization requires mono 16 kHz 16-bit PCM audio.");

        var config = new SpeakerEmbeddingExtractorConfig
        {
            Model = modelPath,
            NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            Debug = 0,
            Provider = "cpu",
        };
        using var extractor = new SpeakerEmbeddingExtractor(config);
        var results = new float[]?[lines.Count];
        var bytesPerSample = reader.WaveFormat.BlockAlign;
        var sampleCount = reader.Length / bytesPerSample;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var startSample = Math.Max(0, (long)Math.Floor(line.StartSeconds * SampleRate));
            var endSample = Math.Min(sampleCount, (long)Math.Ceiling(line.EndSeconds * SampleRate));
            var count = endSample - startSample;
            if (count < MinimumSamples || count > int.MaxValue / bytesPerSample) continue;

            var bytes = new byte[(int)(count * bytesPerSample)];
            reader.Position = startSample * bytesPerSample;
            if (!ReadExactly(reader, bytes)) continue;

            var samples = new float[count];
            for (var sample = 0; sample < samples.Length; sample++)
            {
                var pcm = (short)(bytes[sample * 2] | (bytes[sample * 2 + 1] << 8));
                samples[sample] = pcm / 32768f;
            }

            using var stream = extractor.CreateStream();
            stream.AcceptWaveform(SampleRate, samples);
            stream.InputFinished();
            if (extractor.IsReady(stream))
                results[index] = extractor.Compute(stream);
        }

        return results;
    }

    public static bool ApplyEmbeddings(IReadOnlyList<TranscriptLine> lines, IReadOnlyList<float[]?> embeddings)
    {
        var clusterIds = SpeakerEmbeddingClusterer.AssignClusters(embeddings);
        if (!clusterIds.Any(id => id >= 0)) return false;
        SpeakerEmbeddingClusterer.ApplyLabels(lines, clusterIds);
        return true;
    }

    private static bool ReadExactly(WaveFileReader reader, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = reader.Read(buffer, offset, buffer.Length - offset);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }
}
