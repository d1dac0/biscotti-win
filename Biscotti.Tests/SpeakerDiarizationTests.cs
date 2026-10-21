using Biscotti.Core.Models;
using Biscotti.Core.Transcription;
using System.Security.Cryptography;

namespace Biscotti.Tests;

public sealed class SpeakerDiarizationTests
{
    [Fact]
    public void HasSuitableSegments_RequiresAtLeastOneSecondOfSpeech()
    {
        var shortLine = new TranscriptLine { Speaker = "Speaker", Text = "hi", StartSeconds = 0, EndSeconds = 0.8 };
        var longLine = new TranscriptLine { Speaker = "Speaker", Text = "hello", StartSeconds = 0, EndSeconds = 1 };

        Assert.False(SpeakerDiarizer.HasSuitableSegments([shortLine]));
        Assert.True(SpeakerDiarizer.HasSuitableSegments([shortLine, longLine]));
    }

    [Fact]
    public void SpeakerModelSha256_IsPinnedAndRejectsOtherContent()
    {
        Assert.True(SpeakerEmbeddingModelStore.VerifySha256(Convert.FromHexString(SpeakerEmbeddingModelStore.Sha256)));
        Assert.False(SpeakerEmbeddingModelStore.VerifySha256(SHA256.HashData("other model"u8)));
    }

    [Fact]
    public void AssignClusters_GroupsSimilarEmbeddingsAndLeavesMissingSegmentsUnassigned()
    {
        float[] speakerA1 = [1f, 0f, 0f];
        float[] speakerB = [0f, 1f, 0f];
        float[] speakerA2 = [0.99f, 0.1f, 0f];
        float[] invalid = [float.NaN, 0f, 0f];

        var clusters = SpeakerEmbeddingClusterer.AssignClusters(
            [speakerA1, speakerB, speakerA2, null, invalid]);

        Assert.Equal(0, clusters[0]);
        Assert.Equal(1, clusters[1]);
        Assert.Equal(0, clusters[2]);
        Assert.Equal(-1, clusters[3]);
        Assert.Equal(-1, clusters[4]);
    }

    [Fact]
    public void AssignClusters_AvoidsFalseMergeAndFalseSplitForStableSpeakerVectors()
    {
        float[] firstSpeakerTurn = [1f, 0f, 0f];
        float[] differentSpeakerTurn = [0.5f, 0.8660254f, 0f];
        float[] returningSpeakerTurn = [0.96f, 0.28f, 0f];

        var clusters = SpeakerEmbeddingClusterer.AssignClusters(
            [firstSpeakerTurn, differentSpeakerTurn, returningSpeakerTurn]);

        Assert.Equal(0, clusters[0]);
        Assert.Equal(1, clusters[1]);
        Assert.Equal(0, clusters[2]);
    }

    [Fact]
    public void ApplyLabels_UsesFirstAppearanceAndPreservesFallbackLabels()
    {
        var lines = new List<TranscriptLine>
        {
            new() { Speaker = "Speaker 1", Text = "one", StartSeconds = 0, EndSeconds = 1 },
            new() { Speaker = "Speaker 2", Text = "two", StartSeconds = 1, EndSeconds = 2 },
            new() { Speaker = "Speaker 3", Text = "three", StartSeconds = 2, EndSeconds = 3 },
        };

        SpeakerEmbeddingClusterer.ApplyLabels(lines, [1, -1, 0]);

        Assert.Equal("Speaker 1", lines[0].Speaker);
        Assert.Equal("Speaker 2", lines[1].Speaker);
        Assert.Equal("Speaker 2", lines[2].Speaker);
    }

    [Fact]
    public void SpeakerIntroductionResolver_RenamesOnlyUnambiguousSelfIntroduction()
    {
        var transcript = new List<TranscriptLine>
        {
            new() { Speaker = "Speaker 1", Text = "Hi, I'm Sofia and I manage the project.", StartSeconds = 0, EndSeconds = 3 },
            new() { Speaker = "Speaker 1", Text = "Let's get started.", StartSeconds = 4, EndSeconds = 5 },
            new() { Speaker = "Speaker 2", Text = "My name is Alex.", StartSeconds = 6, EndSeconds = 8 },
            new() { Speaker = "Speaker 3", Text = "I'm happy with the draft.", StartSeconds = 8, EndSeconds = 9 },
            new() { Speaker = "You", Text = "I'm the host.", StartSeconds = 9, EndSeconds = 10 },
        };

        SpeakerIntroductionResolver.Apply(transcript);

        Assert.Equal("Sofia", transcript[0].Speaker);
        Assert.Equal("Sofia", transcript[1].Speaker);
        Assert.Equal("Alex", transcript[2].Speaker);
        Assert.Equal("Speaker 3", transcript[3].Speaker);
        Assert.Equal("You", transcript[4].Speaker);
    }

    [Fact]
    public void SpeakerIntroductionResolver_LeavesConflictingIntroductionsUnchanged()
    {
        var transcript = new List<TranscriptLine>
        {
            new() { Speaker = "Speaker 1", Text = "I am Alex.", StartSeconds = 0, EndSeconds = 1 },
            new() { Speaker = "Speaker 1", Text = "My name is Jamie.", StartSeconds = 2, EndSeconds = 3 },
        };

        SpeakerIntroductionResolver.Apply(transcript);

        Assert.All(transcript, line => Assert.Equal("Speaker 1", line.Speaker));
    }
}
