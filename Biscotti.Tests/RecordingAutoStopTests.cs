using Biscotti.Core;
using Biscotti.Core.Audio;
using System.Text.Json;

namespace Biscotti.Tests;

public sealed class RecordingAutoStopTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Audible = BitConverter.GetBytes(0.02f);
    private static RecordingAutoStop AudibleRecording()
    {
        var policy = new RecordingAutoStop();
        policy.Begin(true);
        policy.Observe(Audible, 4, 1, Start);
        return policy;
    }

    [Fact]
    public void QuietLongCallGetsFullCountdownAndKeepRecordingLastsUntilManualStop()
    {
        var policy = AudibleRecording();
        Assert.Null(policy.Poll(Start.AddMinutes(4)).SecondsRemaining);
        Assert.Equal(30, policy.Poll(Start.AddMinutes(5)).SecondsRemaining);
        Assert.Equal(1, policy.Poll(Start.AddMinutes(5).AddSeconds(29)).SecondsRemaining);
        policy.KeepRecording();
        var quietHour = policy.Poll(Start.AddHours(1));
        Assert.Null(quietHour.SecondsRemaining);
        Assert.True(quietHour.KeptRecording);
        Assert.False(quietHour.ShouldStop);
        policy.Observe(Audible, 4, 1, Start.AddHours(1));
        Assert.False(policy.Poll(Start.AddHours(2)).ShouldStop);
        Assert.False(policy.CanStop);
    }

    [Fact]
    public void CountdownExpiresOnceAndCanBeInvalidatedBeforeQueuedStopCommits()
    {
        var policy = AudibleRecording();
        policy.Poll(Start.AddMinutes(5));
        Assert.True(policy.Poll(Start.AddMinutes(5).AddSeconds(30)).ShouldStop);
        Assert.True(policy.CanStop);
        Assert.False(policy.Poll(Start.AddMinutes(6)).ShouldStop);
        policy.SetEnabled(false);
        Assert.False(policy.CanStop);
    }

    [Fact]
    public void KeepRecordingWinsIfStopHasBeenQueuedButNotCommitted()
    {
        var policy = AudibleRecording();
        policy.Poll(Start.AddMinutes(5));
        Assert.True(policy.Poll(Start.AddMinutes(5).AddSeconds(30)).ShouldStop);
        policy.KeepRecording();
        Assert.False(policy.CanStop);
        Assert.True(policy.Poll(Start.AddHours(1)).KeptRecording);
    }

    [Fact]
    public void MicrophoneOnlyMeetingNeverArmsEvenAfterHours()
    {
        var policy = new RecordingAutoStop();
        policy.Begin(true);
        for (var minute = 0; minute < 180; minute++)
        {
            policy.Observe(BitConverter.GetBytes(0.001f), 4, 1, Start.AddMinutes(minute));
            var notice = policy.Poll(Start.AddMinutes(minute));
            Assert.False(notice.IsArmed);
            Assert.Null(notice.SecondsRemaining);
            Assert.False(notice.ShouldStop);
        }
    }

    [Fact]
    public void OutputResumptionClearsNoticeAndStartsNewSilenceInterval()
    {
        var policy = AudibleRecording();
        Assert.Equal(30, policy.Poll(Start.AddMinutes(5)).SecondsRemaining);
        policy.Observe(Audible, 4, 1, Start.AddMinutes(5).AddSeconds(29));
        Assert.Null(policy.Poll(Start.AddMinutes(6)).SecondsRemaining);
        Assert.Equal(30, policy.Poll(Start.AddMinutes(10).AddSeconds(29)).SecondsRemaining);
    }

    [Fact]
    public void DisablingClearsCountdownAndReenablingRequiresNewOutput()
    {
        var policy = AudibleRecording();
        policy.Poll(Start.AddMinutes(5));
        policy.SetEnabled(false);
        Assert.False(policy.Poll(Start.AddMinutes(6)).Enabled);
        Assert.Null(policy.Poll(Start.AddMinutes(6)).SecondsRemaining);
        policy.Observe(Audible, 4, 1, Start.AddMinutes(7));
        policy.SetEnabled(true);
        Assert.False(policy.Poll(Start.AddHours(1)).IsArmed);
        policy.Observe(Audible, 4, 1, Start.AddHours(1));
        Assert.Equal(30, policy.Poll(Start.AddHours(1).AddMinutes(5)).SecondsRemaining);
    }

    [Fact]
    public void AppCloseAndReopenDoesNotReuseCountdownOrSessionOverride()
    {
        var policy = AudibleRecording();
        policy.Poll(Start.AddMinutes(5));
        policy.End(); // shutdown/manual stop
        policy.Observe(Audible, 4, 1, Start.AddMinutes(6)); // late capture packet
        Assert.Null(policy.Poll(Start.AddHours(1)).SecondsRemaining);
        Assert.False(policy.CanStop);
        policy.Begin(true); // a new recording, including after process restart
        Assert.False(policy.Poll(Start.AddHours(1)).IsArmed);
        policy.KeepRecording();
        policy.End();
        policy.Begin(true);
        Assert.False(policy.Poll(Start.AddHours(2)).KeptRecording);
    }

    [Fact]
    public void MeetingAppExitOrReopenHasNoAuthorityToStopAndDelayedTickGivesFullWarning()
    {
        // No process event is an input: app restart cannot become a new stop trigger.
        var policy = AudibleRecording();
        Assert.Equal(30, policy.Poll(Start.AddHours(1)).SecondsRemaining);
        Assert.False(policy.Poll(Start.AddHours(1).AddSeconds(29)).ShouldStop);
        policy.Observe(Audible, 4, 1, Start.AddHours(1).AddSeconds(29));
        Assert.Null(policy.Poll(Start.AddHours(1).AddSeconds(30)).SecondsRemaining);
    }

    [Fact]
    public void PreferenceDefaultsOnAndPersistsAcrossAppReopenWithoutCountdownState()
    {
        Assert.True(JsonSerializer.Deserialize<AppSettings>("{}")!.AutoStopEnabled);
        var serialized = JsonSerializer.Serialize(new AppSettings { AutoStopEnabled = false });
        var reopened = JsonSerializer.Deserialize<AppSettings>(serialized)!;
        Assert.False(reopened.AutoStopEnabled);
        var policy = new RecordingAutoStop();
        policy.Begin(reopened.AutoStopEnabled);
        policy.Observe(Audible, 4, 1, Start);
        Assert.False(policy.Poll(Start.AddHours(1)).ShouldStop);
    }
}
