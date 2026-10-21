using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Biscotti.Core.Models;
using Biscotti.Core.Transcription;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: SpeakerEvaluation <mono-16k-wav> <segments-directory> <speaker-model.onnx> [noise-snr-db,...]");
    return 2;
}

var wavPath = Path.GetFullPath(args[0]);
var annotationDirectory = Path.GetFullPath(args[1]);
var modelPath = Path.GetFullPath(args[2]);
var noiseSnrs = args.Length > 3
    ? args[3].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray()
    : [10d, 0d];

var meetingId = Path.GetFileName(wavPath).Split('.')[0];
var reference = LoadTurns(annotationDirectory, meetingId);
var originalWav = await File.ReadAllBytesAsync(wavPath);
ValidateWav(originalWav);

var conditions = new List<(string Name, byte[] Wav)> { ("clean", originalWav) };
conditions.AddRange(noiseSnrs.Select(snr => ($"white-noise-{snr.ToString(CultureInfo.InvariantCulture)}-dB-SNR", AddWhiteNoise(originalWav, snr))));

var outputs = new List<ConditionResult>();
foreach (var (name, wav) in conditions)
{
    Console.Error.WriteLine($"Extracting embeddings: {name} ({reference.Count} annotated turns)...");
    using var stream = new MemoryStream(wav, writable: false);
    var embeddings = SpeakerDiarizer.ExtractEmbeddings(stream, reference.Select(turn => turn.Line).ToArray(), modelPath);
    outputs.Add(Evaluate(name, reference, embeddings));
}

Console.WriteLine(JsonSerializer.Serialize(new EvaluationResult(
    Path.GetFileName(wavPath), reference.Count,
    reference.Count(turn => turn.Line.EndSeconds - turn.Line.StartSeconds < 1.0),
    reference.Count(turn => turn.OverlapsAnotherSpeaker), outputs),
    new JsonSerializerOptions { WriteIndented = true }));
return 0;

static List<ReferenceTurn> LoadTurns(string directory, string meetingId)
{
    var turns = new List<ReferenceTurn>();
    foreach (var file in Directory.GetFiles(directory, $"{meetingId}.*.segments.xml").Order(StringComparer.OrdinalIgnoreCase))
    {
        var speaker = Path.GetFileName(file).Split('.')[1];
        var document = XDocument.Load(file);
        foreach (var segment in document.Descendants().Where(element => element.Name.LocalName == "segment"))
        {
            var start = double.Parse((string)segment.Attribute("transcriber_start")!, CultureInfo.InvariantCulture);
            var end = double.Parse((string)segment.Attribute("transcriber_end")!, CultureInfo.InvariantCulture);
            turns.Add(new ReferenceTurn(speaker, start, end,
                new TranscriptLine { Speaker = speaker, Text = "reference", StartSeconds = start, EndSeconds = end }));
        }
    }

    turns.Sort((left, right) => left.Start.CompareTo(right.Start));
    for (var first = 0; first < turns.Count; first++)
    for (var second = first + 1; second < turns.Count && turns[second].Start < turns[first].End; second++)
    {
        if (turns[first].Speaker == turns[second].Speaker || Math.Min(turns[first].End, turns[second].End) <= Math.Max(turns[first].Start, turns[second].Start)) continue;
        turns[first].OverlapsAnotherSpeaker = true;
        turns[second].OverlapsAnotherSpeaker = true;
    }

    return turns;
}

static ConditionResult Evaluate(string name, IReadOnlyList<ReferenceTurn> turns, IReadOnlyList<float[]?> embeddings)
{
    var usable = turns.Select((turn, index) => (turn, embedding: embeddings[index], index))
        .Where(item => item.embedding is { Length: > 0 }).ToArray();
    var fallback = turns.Count - usable.Length;
    var thresholds = Enumerable.Range(70, 29).Select(value => value / 100f)
        .Select(threshold => Measure(usable, SpeakerEmbeddingClusterer.AssignClusters(embeddings, threshold), threshold)).ToArray();
    var selected = thresholds.Single(metric => Math.Abs(metric.Threshold - SpeakerEmbeddingClusterer.SimilarityThreshold) < 0.0001f);
    var best = thresholds.OrderByDescending(metric => metric.F1).ThenByDescending(metric => metric.Precision).ThenBy(metric => Math.Abs(metric.Threshold - selected.Threshold)).First();
    var overlapIndexes = usable.Select((item, position) => (item, position)).Where(value => value.item.turn.OverlapsAnotherSpeaker).Select(value => value.position).ToHashSet();
    var isolatedOnlyMetrics = PairMetrics(usable, SpeakerEmbeddingClusterer.AssignClusters(embeddings, selected.Threshold), selected.Threshold,
        (first, second) => !overlapIndexes.Contains(first) && !overlapIndexes.Contains(second));
    var overlapOnlyMetrics = PairMetrics(usable, SpeakerEmbeddingClusterer.AssignClusters(embeddings, selected.Threshold), selected.Threshold,
        (first, second) => overlapIndexes.Contains(first) || overlapIndexes.Contains(second));

    return new ConditionResult(name, usable.Length, fallback, selected, best, isolatedOnlyMetrics, overlapOnlyMetrics, thresholds);
}

static Metric Measure((ReferenceTurn turn, float[]? embedding, int index)[] usable, int[] clusters, float threshold) =>
    PairMetrics(usable, clusters, threshold, static (_, _) => true);

static Metric PairMetrics(
    (ReferenceTurn turn, float[]? embedding, int index)[] usable,
    int[] clusters,
    float threshold,
    Func<int, int, bool> include)
{
    long trueSame = 0, predictedSame = 0, truePositive = 0;
    for (var first = 0; first < usable.Length; first++)
    for (var second = first + 1; second < usable.Length; second++)
    {
        if (!include(first, second)) continue;
        var sameSpeaker = usable[first].turn.Speaker == usable[second].turn.Speaker;
        var sameCluster = clusters[usable[first].index] >= 0 && clusters[usable[first].index] == clusters[usable[second].index];
        if (sameSpeaker) trueSame++;
        if (sameCluster) predictedSame++;
        if (sameSpeaker && sameCluster) truePositive++;
    }

    var precision = predictedSame == 0 ? 1 : (double)truePositive / predictedSame;
    var recall = trueSame == 0 ? 1 : (double)truePositive / trueSame;
    var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
    return new Metric(threshold, precision, recall, f1, truePositive, predictedSame - truePositive, trueSame - truePositive);
}

static void ValidateWav(byte[] wav)
{
    if (wav.Length < 44 || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wav.AsSpan(8, 4).SequenceEqual("WAVE"u8) ||
        BitConverter.ToUInt16(wav, 20) != 1 || BitConverter.ToUInt16(wav, 22) != 1 ||
        BitConverter.ToUInt32(wav, 24) != 16_000 || BitConverter.ToUInt16(wav, 34) != 16 ||
        !wav.AsSpan(36, 4).SequenceEqual("data"u8))
        throw new InvalidDataException("Expected a standard mono 16 kHz 16-bit PCM WAV with a 44-byte header.");
}

static byte[] AddWhiteNoise(byte[] original, double snrDb)
{
    if (snrDb < -10 || snrDb > 40) throw new ArgumentOutOfRangeException(nameof(snrDb), "SNR must be between -10 and 40 dB.");
    var result = (byte[])original.Clone();
    var sampleCount = (result.Length - 44) / 2;
    double signalSquares = 0;
    for (var index = 0; index < sampleCount; index++)
    {
        var sample = (short)(original[44 + index * 2] | (original[45 + index * 2] << 8));
        signalSquares += sample * (double)sample;
    }

    var noiseSigma = Math.Sqrt(signalSquares / sampleCount / Math.Pow(10, snrDb / 10));
    var random = new Random(70210);
    double maximum = 1;
    var noisy = new double[sampleCount];
    for (var index = 0; index < sampleCount; index++)
    {
        var sample = (short)(original[44 + index * 2] | (original[45 + index * 2] << 8));
        noisy[index] = sample + noiseSigma * NextGaussian(random);
        maximum = Math.Max(maximum, Math.Abs(noisy[index]));
    }

    var gain = Math.Min(1, short.MaxValue / maximum);
    for (var index = 0; index < sampleCount; index++)
    {
        var sample = (short)Math.Clamp((int)Math.Round(noisy[index] * gain), short.MinValue, short.MaxValue);
        result[44 + index * 2] = (byte)sample;
        result[45 + index * 2] = (byte)(sample >> 8);
    }

    return result;
}

static double NextGaussian(Random random)
{
    var first = 1 - random.NextDouble();
    var second = random.NextDouble();
    return Math.Sqrt(-2 * Math.Log(first)) * Math.Cos(2 * Math.PI * second);
}

sealed record EvaluationResult(string AudioFile, int AnnotatedTurns, int ShortTurnFallbacks, int OverlappingTurns, IReadOnlyList<ConditionResult> Conditions);
sealed record ConditionResult(string Name, int EmbeddedTurns, int FallbackTurns, Metric AtCurrentThreshold, Metric BestThreshold, Metric NonOverlappingPairMetrics, Metric OverlapPairMetrics, IReadOnlyList<Metric> ThresholdSweep);
sealed record Metric(float Threshold, double Precision, double Recall, double F1, long TruePositivePairs, long FalseMergePairs, long FalseSplitPairs);

sealed class ReferenceTurn(string speaker, double start, double end, TranscriptLine line)
{
    public string Speaker { get; } = speaker;
    public double Start { get; } = start;
    public double End { get; } = end;
    public TranscriptLine Line { get; } = line;
    public bool OverlapsAnotherSpeaker { get; set; }
}
