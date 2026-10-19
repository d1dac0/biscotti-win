using System.Buffers.Binary;
using System.Text.Json;
using Biscotti.Core.Audio;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;
using Xunit;

namespace Biscotti.Tests;

/// <summary>
/// Regression coverage for a forced kill mid-meeting (issue #12): the WAV on
/// disk must be readable without Close(), and meeting.json must never be left
/// half-written by a re-save.
/// </summary>
public class CrashSafetyTests
{
    [Fact]
    public void Unclosed_WavWriter_Header_MatchesDataAlreadyOnDisk()
    {
        var path = TempWavPath();
        var writer = new WavStreamWriter(path);
        try
        {
            writer.Append(new byte[1000]);

            // Deliberately no Close(): this is what a killed process leaves.
            var bytes = ReadAllBytesShared(path);
            Assert.Equal(44 + 1000, bytes.Length);
            Assert.Equal((uint)(36 + 1000), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
            Assert.Equal((uint)1000, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(40)));

            writer.Append(new byte[500]);
            bytes = ReadAllBytesShared(path);
            Assert.Equal((uint)(36 + 1500), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
            Assert.Equal((uint)1500, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(40)));
        }
        finally
        {
            writer.Dispose();
            File.Delete(path);
        }
    }

    [Fact]
    public void Closed_WavWriter_KeepsFinalSizes()
    {
        var path = TempWavPath();
        try
        {
            using (var writer = new WavStreamWriter(path))
                writer.Append(new byte[777]);

            var bytes = File.ReadAllBytes(path);
            Assert.Equal((uint)(36 + 777), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
            Assert.Equal((uint)777, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(40)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MeetingStore_ReSave_RoundTripsAndLeavesNoTempFiles()
    {
        using var root = new TempMeetingRoot();
        var meeting = new Meeting
        {
            StartedUtc = DateTime.UtcNow,
            Title = "First title",
        };
        meeting.Transcript.Add(new TranscriptLine
        {
            Speaker = "You",
            Text = "hello",
            StartSeconds = 0.5,
            EndSeconds = 1.2,
        });

        MeetingStore.Save(meeting);
        AssertValidSavedMeeting(meeting, "First title");

        // Re-save the same meeting (the crash window the issue describes).
        meeting.Title = "Second title";
        MeetingStore.Save(meeting);
        AssertValidSavedMeeting(meeting, "Second title");

        var dir = MeetingStore.MeetingDir(meeting.Id);
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact]
    public void MeetingStore_LoadAll_IgnoresStaleTempFileFromCrashedSave()
    {
        using var root = new TempMeetingRoot();
        var meeting = new Meeting { StartedUtc = DateTime.UtcNow, Title = "Survivor" };
        MeetingStore.Save(meeting);

        // A kill between temp-write and rename leaves a .tmp beside a valid
        // meeting.json; the loader must not treat the temp file as the meeting.
        var dir = MeetingStore.MeetingDir(meeting.Id);
        File.WriteAllText(Path.Combine(dir, "meeting.json.tmp"), "{\"Title\":\"trunc");

        var loaded = MeetingStore.LoadAll();
        var match = Assert.Single(loaded);
        Assert.Equal("Survivor", match.Title);
    }

    private static void AssertValidSavedMeeting(Meeting meeting, string expectedTitle)
    {
        var dir = MeetingStore.MeetingDir(meeting.Id);
        var jsonPath = Path.Combine(dir, "meeting.json");
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(Path.Combine(dir, "meeting.md")));

        var parsed = JsonSerializer.Deserialize<Meeting>(File.ReadAllText(jsonPath));
        Assert.NotNull(parsed);
        Assert.Equal(expectedTitle, parsed!.Title);
        Assert.Single(parsed.Transcript);

        var loaded = Assert.Single(MeetingStore.LoadAll());
        Assert.Equal(expectedTitle, loaded.Title);
    }

    private static string TempWavPath() =>
        Path.Combine(Path.GetTempPath(), $"biscotti-{Guid.NewGuid():N}.wav");

    /// <summary>
    /// Reads a WAV that the writer still has open with FileShare.Read. A reader
    /// must allow the writer's write access, otherwise Windows denies the open
    /// (which is exactly why File.ReadAllBytes fails here).
    /// </summary>
    private static byte[] ReadAllBytesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var buffer = new MemoryStream();
        fs.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Points MeetingStore at a throwaway directory, then restores it.</summary>
    private sealed class TempMeetingRoot : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"biscotti-tests-{Guid.NewGuid():N}");

        public TempMeetingRoot() => MeetingStore.RootOverride = _root;

        public void Dispose()
        {
            MeetingStore.RootOverride = null;
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
