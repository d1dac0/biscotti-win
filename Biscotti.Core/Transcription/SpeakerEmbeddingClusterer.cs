using Biscotti.Core.Models;

namespace Biscotti.Core.Transcription;

/// <summary>Groups transcript segments by cosine similarity of voice embeddings.</summary>
public static class SpeakerEmbeddingClusterer
{
    public const float SimilarityThreshold = 0.88f;

    /// <summary>
    /// Assigns stable per-meeting labels in order of first appearance. A null
    /// embedding leaves the line's existing fallback label unchanged.
    /// </summary>
    public static int[] AssignClusters(IReadOnlyList<float[]?> embeddings, float threshold = SimilarityThreshold)
    {
        var result = Enumerable.Repeat(-1, embeddings.Count).ToArray();
        var clusters = new List<Cluster>();

        for (var index = 0; index < embeddings.Count; index++)
        {
            var embedding = embeddings[index];
            if (embedding is null || !IsUsable(embedding)) continue;

            var bestIndex = -1;
            var bestScore = float.NegativeInfinity;
            for (var clusterIndex = 0; clusterIndex < clusters.Count; clusterIndex++)
            {
                var score = CosineSimilarity(embedding, clusters[clusterIndex].Centroid);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = clusterIndex;
                }
            }

            if (bestIndex < 0 || bestScore < threshold)
            {
                bestIndex = clusters.Count;
                clusters.Add(new Cluster(embedding));
            }
            else
            {
                clusters[bestIndex].Add(embedding);
            }

            result[index] = bestIndex;
        }

        return result;
    }

    public static void ApplyLabels(IReadOnlyList<TranscriptLine> lines, IReadOnlyList<int> clusterIds)
    {
        if (lines.Count != clusterIds.Count)
            throw new ArgumentException("Each transcript line must have one cluster result.", nameof(clusterIds));

        var clusterOrder = clusterIds
            .Select((id, index) => (id, index))
            .Where(item => item.id >= 0)
            .GroupBy(item => item.id)
            .OrderBy(group => group.Min(item => item.index))
            .Select((group, order) => (group.Key, Label: $"Speaker {order + 1}"))
            .ToDictionary(item => item.Key, item => item.Label);

        for (var index = 0; index < lines.Count; index++)
            if (clusterOrder.TryGetValue(clusterIds[index], out var label))
            {
                lines[index].Speaker = label;
                lines[index].SpeakerKey = $"system:{clusterIds[index]}";
            }
    }

    public static float CosineSimilarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length || left.Length == 0) return 0;
        double dot = 0, leftNorm = 0, rightNorm = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
        }

        return leftNorm <= 0 || rightNorm <= 0
            ? 0
            : (float)(dot / Math.Sqrt(leftNorm * rightNorm));
    }

    private static bool IsUsable(float[] embedding) =>
        embedding.Length > 0 && embedding.All(float.IsFinite) && embedding.Any(value => value != 0);

    private sealed class Cluster(float[] first)
    {
        private readonly double[] _sum = first.Select(value => (double)value).ToArray();
        private int _count = 1;

        public float[] Centroid => Normalize(_sum, _count);

        public void Add(float[] embedding)
        {
            for (var index = 0; index < _sum.Length; index++) _sum[index] += embedding[index];
            _count++;
        }

        private static float[] Normalize(double[] sum, int count)
        {
            var result = new float[sum.Length];
            double norm = 0;
            for (var index = 0; index < sum.Length; index++)
            {
                result[index] = (float)(sum[index] / count);
                norm += result[index] * result[index];
            }

            if (norm > 0)
            {
                var magnitude = Math.Sqrt(norm);
                for (var index = 0; index < result.Length; index++) result[index] /= (float)magnitude;
            }
            return result;
        }
    }
}
