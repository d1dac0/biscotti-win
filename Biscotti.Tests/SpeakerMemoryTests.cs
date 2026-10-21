using Biscotti.Core.Models;
using Biscotti.Core.Storage;
using Biscotti.Core.Transcription;

namespace Biscotti.Tests;

[Collection("Speaker memory DPAPI isolation")]
public sealed class SpeakerMemoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "biscotti-speaker-tests-" + Guid.NewGuid());
    private string StorePath => Path.Combine(_root, "speaker-memory.dat");
    private SpeakerMemoryStore Store => new(StorePath);
    private static float[][] VoiceA => [[1, 0, 0], [0.999f, 0.01f, 0]];
    private static float[][] VoiceB => [[0, 1, 0], [0.01f, 0.999f, 0]];

    [Fact]
    public void EnrollmentRequiresOptInAndStoresOnlyEncryptedContent()
    {
        Assert.False(Store.Load().Enabled);
        Assert.Throws<InvalidOperationException>(() => Store.Remember(Guid.NewGuid(), "system:0", "Alex Private", VoiceA));
        Assert.False(File.Exists(StorePath));
        Store.SetEnabled(true);
        var id = Store.Remember(Guid.NewGuid(), "system:0", "Alex Private", VoiceA);
        Assert.DoesNotContain("Alex Private", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(StorePath)));
        var loaded = new SpeakerMemoryStore(StorePath).Load();
        Assert.Equal(id, Assert.Single(loaded.Voices).PersonId);
        Assert.Equal("Alex Private", loaded.Voices[0].Name);
        Assert.True(loaded.Enabled);
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void CorrectionsKeepStableGroupsSeparateEvenWhenTheirNamesCoincide()
    {
        var meeting = new Meeting { Transcript = [Line("Speaker 1", "system:0"), Line("Speaker 2", "system:1"), Line("Speaker 1", "system:0")] };
        SpeakerMemory.Correct(meeting, "system:0", "Alex");
        SpeakerMemory.Correct(meeting, "system:1", "Alex");
        SpeakerMemory.Correct(meeting, "system:0", "Jordan");
        Assert.Equal(new[] { "Jordan", "Alex", "Jordan" }, meeting.Transcript.Select(line => line.Speaker));
        Assert.Equal(2, SpeakerMemory.Groups(meeting).Count);
        Assert.All(SpeakerMemory.Groups(meeting), group => Assert.True(group.CanRemember));
    }

    [Fact]
    public void LegacyCorrectionFreezesGroupButDoesNotAllowEnrollment()
    {
        var meeting = new Meeting { Transcript = [Line("Speaker", null), Line("You", null)] };
        SpeakerMemory.Correct(meeting, "legacy:Speaker", "Alex");
        SpeakerMemory.Correct(meeting, "legacy:Speaker", "Jordan");
        Assert.Equal("Jordan", meeting.Transcript[0].Speaker);
        Assert.Equal("You", meeting.Transcript[1].Speaker);
        Assert.False(SpeakerMemory.Groups(meeting)[0].CanRemember);
        Assert.Throws<ArgumentException>(() => Store.Remember(meeting.Id, "legacy:Speaker", "Jordan", VoiceA));
    }

    [Fact]
    public void StrongReturningVoiceSuggestsPersonButNeverChangesTranscript()
    {
        Store.SetEnabled(true);
        var source = Guid.NewGuid();
        var id = Store.Remember(source, "system:0", "Alex", VoiceA);
        Store.Remember(Guid.NewGuid(), "system:0", "Jordan", VoiceB);
        var target = new Meeting { Transcript = [Line("Speaker 1", "system:0")] };
        var suggestion = SpeakerMemory.Suggest(target.Id, VoiceA, Store.Load().Voices);
        Assert.Equal(id, suggestion!.PersonId);
        Assert.Equal("Speaker 1", target.Transcript[0].Speaker);
        Assert.Null(SpeakerMemory.Suggest(source, VoiceA, Store.Load().Voices));
    }

    [Fact]
    public void MissingWeakInvalidAndConflictingEvidenceAbstains()
    {
        Store.SetEnabled(true);
        Store.Remember(Guid.NewGuid(), "system:0", "Alex", VoiceA);
        Store.Remember(Guid.NewGuid(), "system:0", "Jordan", VoiceB);
        var voices = Store.Load().Voices;
        var target = Guid.NewGuid();
        Assert.Null(SpeakerMemory.Suggest(target, [], voices));
        Assert.Null(SpeakerMemory.Suggest(target, [[1, 0, 0]], voices));
        Assert.Null(SpeakerMemory.Suggest(target, [[0, 0, 1], [0, 0, 1]], voices));
        Assert.Null(SpeakerMemory.Suggest(target, [[1, 0, 0], [0, 1, 0]], voices));
        Assert.Null(SpeakerMemory.Suggest(target, [[float.NaN, 0, 0], [1, 0, 0]], voices));
        Assert.Null(SpeakerMemory.Suggest(target, [[1, 0], [1, 0]], voices));
        Assert.Null(SpeakerMemory.Suggest(target, VoiceA, []));
    }

    [Fact]
    public void NearIdenticalDifferentPeopleAreAmbiguous()
    {
        Store.SetEnabled(true);
        Store.Remember(Guid.NewGuid(), "system:0", "Alex", VoiceA);
        Store.Remember(Guid.NewGuid(), "system:0", "Jordan", [[0.99f, 0.1f, 0], [0.99f, 0.11f, 0]]);
        Assert.Null(SpeakerMemory.Suggest(Guid.NewGuid(), VoiceA, Store.Load().Voices));
    }

    [Fact]
    public void ReassigningSameMeetingGroupReplacesMistakeAndForgetRemovesEverySample()
    {
        Store.SetEnabled(true);
        var source = Guid.NewGuid();
        Store.Remember(source, "system:0", "Wrong person", VoiceA);
        var correct = Store.Remember(Guid.NewGuid(), "system:0", "Correct person", VoiceA);
        Store.Remember(source, "system:0", "ignored stale name", VoiceA, correct);
        var state = Store.Load();
        Assert.Equal(2, state.Voices.Count);
        Assert.All(state.Voices, voice => Assert.Equal("Correct person", voice.Name));
        Store.Forget(correct);
        Assert.Empty(Store.Load().Voices);
        Assert.Throws<ArgumentException>(() => Store.Remember(source, "system:0", "Stale", VoiceA, correct));
        Store.SetEnabled(false);
        Assert.False(File.Exists(StorePath));
        Assert.False(Store.Load().Enabled);
    }

    [Fact]
    public void InvalidEnrollmentNeverWritesAndCorruptionIsPreserved()
    {
        Store.SetEnabled(true);
        Assert.Throws<ArgumentException>(() => Store.Remember(Guid.NewGuid(), "system:0", "Alex", [[1, 0]]));
        Assert.Throws<ArgumentException>(() => Store.Remember(Guid.NewGuid(), "system:0", "Alex", [[1, 0], [0, 0]]));
        Assert.Throws<ArgumentException>(() => Store.Remember(Guid.NewGuid(), "system:0", "\n", VoiceA));
        Assert.Empty(Store.Load().Voices);
        var bytes = File.ReadAllBytes(StorePath);
        bytes[^1] ^= 0xff;
        File.WriteAllBytes(StorePath, bytes);
        Assert.Throws<MeetingTextStorageException>(() => Store.Load());
        Assert.Throws<MeetingTextStorageException>(() => Store.SetEnabled(true));
        Assert.Equal(bytes, File.ReadAllBytes(StorePath));
        Store.SetEnabled(false); // Forget remains possible even when the profile/key is unavailable.
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public void SyntheticImpostorSweepHasNoSuggestionsAndGenuineSweepAgrees()
    {
        var voices = new[]
        {
            new RememberedVoice(Guid.NewGuid(), "A", Guid.NewGuid(), "system:0", VoiceA),
            new RememberedVoice(Guid.NewGuid(), "B", Guid.NewGuid(), "system:0", VoiceB),
        };
        var target = Guid.NewGuid();
        // 100 unseen synthetic vectors dominated by an orthogonal component.
        for (var index = 0; index < 100; index++)
        {
            float offset = index / 500f;
            Assert.Null(SpeakerMemory.Suggest(target, [[offset, 0.2f - offset, 1], [offset, 0.2f - offset, 1]], voices));
        }
        for (var index = 0; index < 20; index++)
        {
            float offset = index / 500f;
            Assert.Equal(voices[0].PersonId,
                SpeakerMemory.Suggest(target, [[1, offset, 0], [1, offset, 0]], voices)!.PersonId);
        }
    }

    private static TranscriptLine Line(string name, string? key) => new()
    { Speaker = name, SpeakerKey = key, Text = "Synthetic speech.", StartSeconds = 0, EndSeconds = 3 };

    [Fact]
    public void CorrectedGroupsRoundTripThroughProtectedMeetingStorage()
    {
        var previousRoot = MeetingStore.RootOverride;
        try
        {
            MeetingStore.RootOverride = Path.Combine(_root, "Meetings");
            var meeting = new Meeting { Summary = "Original summary", Transcript = [Line("Speaker 1", "system:0")] };
            SpeakerMemory.Correct(meeting, "system:0", "Alex");
            MeetingStore.Save(meeting);
            var loaded = Assert.Single(MeetingStore.LoadAll());
            Assert.Equal("system:0", loaded.Transcript[0].SpeakerKey);
            Assert.Equal("Alex", loaded.Transcript[0].Speaker);
            Assert.Equal("Original summary", loaded.Summary);
            Assert.DoesNotContain("Alex", File.ReadAllText(Path.Combine(MeetingStore.MeetingDir(meeting.Id), "meeting.json")));
            Assert.False(Store.Load().Enabled);
        }
        finally { MeetingStore.RootOverride = previousRoot; }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}

[CollectionDefinition("Speaker memory DPAPI isolation", DisableParallelization = true)]
public sealed class SpeakerMemoryTestCollection;
