using Biscotti.Core;
using Biscotti.Core.Audio;
using Xunit;

namespace Biscotti.Tests;

public sealed class AudioDeviceTests
{
    private static AppSettings Selection(string? output = null) => new()
        { MicDeviceIndex = 1, MicDeviceName = "Headset", SpeakerDeviceId = output };

    private static AudioDeviceSnapshot Snapshot(string? defaultId = "speakers", bool microphone = true,
        bool speakers = true) => new(
        microphone ? [new(0, "Webcam"), new(1, "Headset")] : [new(0, "Webcam")],
        speakers ? [new(null, "Windows default output"), new("speakers", "Speakers"), new("headphones", "Headphones")]
            : [new(null, "Windows default output")], defaultId);

    [Fact]
    public void SavedMicrophoneMustMatchBothIndexAndName()
    {
        var devices = Snapshot();
        AudioDevices.Validate(Selection(), devices);
        var moved = devices with { Microphones = [new(0, "Headset"), new(1, "Webcam")] };
        Assert.Throws<AudioDeviceException>(() => AudioDevices.Validate(Selection(), moved));
        Assert.Throws<AudioDeviceException>(() => AudioDevices.Validate(new AppSettings(), devices));
    }

    [Fact]
    public void SavedMicrophoneEndpointMustStillMatchWhenItWasRecorded()
    {
        var settings = new AppSettings
        {
            MicDeviceIndex = 1, MicDeviceName = "Headset", MicDeviceEndpointId = "mic-endpoint"
        };
        var devices = new AudioDeviceSnapshot(
            [new(0, "Webcam"), new(1, "Headset", "mic-endpoint")],
            Snapshot().Outputs, "speakers");
        AudioDevices.Validate(settings, devices);
        Assert.Throws<AudioDeviceException>(() => AudioDevices.Validate(settings,
            devices with { Microphones = [new(0, "Webcam"), new(1, "Headset", "replacement-mic")] }));
    }

    [Fact]
    public void WaveInMicrophoneMapsOnlyToOneMatchingCoreAudioEndpoint()
    {
        var microphones = new[] { new WaveInDeviceInfo(0, "Headset"), new WaveInDeviceInfo(1, "Webcam") };
        var endpoints = new[] { new CaptureEndpointInfo("mic-1", "headset"), new CaptureEndpointInfo("mic-2", "Webcam") };
        var attached = MicrophoneEndpointMatcher.Attach(microphones, endpoints);
        Assert.Equal("mic-1", attached[0].EndpointId);
        Assert.Equal("mic-2", attached[1].EndpointId);

        var ambiguous = MicrophoneEndpointMatcher.Attach(microphones,
            [new("mic-1", "Headset"), new("mic-2", "HEADSET")]);
        Assert.Null(ambiguous[0].EndpointId);
    }

    [Theory]
    [InlineData(22620, "mic", false)]
    [InlineData(22621, null, false)]
    [InlineData(22621, "mic", true)]
    public void WindowsAecNeedsModernWindowsAndAUniqueCaptureEndpoint(int build, string? micId, bool expected) =>
        Assert.Equal(expected, WindowsEchoCancellationPolicy.CanUseReferenceControl(build, micId));

    [Fact]
    public void AecUnavailableStatusSaysMicrophoneIsRecordedWithoutCancellation()
    {
        Assert.Contains("without echo cancellation", WindowsEchoCancellationPolicy.UnavailableStatus(22620, "mic"));
        Assert.Contains("without echo cancellation", WindowsEchoCancellationPolicy.UnavailableStatus(22621, null));
    }

    [Fact]
    public void MissingExplicitOutputDoesNotFallBackToAvailableDefault()
    {
        Assert.Throws<AudioDeviceException>(() => AudioDevices.Validate(Selection("unplugged"), Snapshot()));
        AudioDevices.Validate(Selection("headphones"), Snapshot(defaultId: null));
        Assert.Throws<AudioDeviceException>(() => AudioDevices.Validate(Selection(), Snapshot(defaultId: null)));
    }

    [Fact]
    public void ExplicitOutputIgnoresDefaultChangesButReportsSelectedEndpointLoss()
    {
        var tracker = new AudioDeviceChangeTracker(Selection("speakers"), Snapshot());
        Assert.Empty(tracker.Observe(Snapshot(defaultId: "headphones")));
        Assert.Contains("disconnected", Assert.Single(tracker.Observe(Snapshot(speakers: false))));
    }

    [Fact]
    public void DefaultOutputChangeAndMicrophoneLossStayReportedOnlyOnce()
    {
        var tracker = new AudioDeviceChangeTracker(Selection(), Snapshot());
        var changed = Snapshot(defaultId: "headphones", microphone: false);
        var warnings = tracker.Observe(changed).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.Contains(warnings, w => w.Contains("microphone"));
        Assert.Contains(warnings, w => w.Contains("default output changed"));
        Assert.Empty(tracker.Observe(changed));
    }

    [Fact]
    public void ChangedMicrophoneEndpointIsReportedEvenWhenItsNameIsReused()
    {
        var settings = new AppSettings
        {
            MicDeviceIndex = 1, MicDeviceName = "Headset", MicDeviceEndpointId = "original-mic"
        };
        var initial = new AudioDeviceSnapshot(
            [new(0, "Webcam"), new(1, "Headset", "original-mic")],
            Snapshot().Outputs, "speakers");
        var tracker = new AudioDeviceChangeTracker(settings, initial);
        var replacement = initial with
        {
            Microphones = [new(0, "Webcam"), new(1, "Headset", "replacement-mic")]
        };

        Assert.Contains("microphone", Assert.Single(tracker.Observe(replacement)));
    }

    [Fact]
    public void DisposedWatchDoesNotEnumerateOrPublishFurtherWarnings()
    {
        var enumerations = 0;
        var warnings = new List<string>();
        var watch = new AudioDeviceWatch(Selection(), Snapshot(), warnings.Add,
            () => { enumerations++; return Snapshot(microphone: false); }, Timeout.InfiniteTimeSpan);
        watch.Poll();
        Assert.Single(warnings);
        watch.Dispose();
        watch.Poll();
        Assert.Equal(1, enumerations);
        Assert.Single(warnings);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedStartupRollsBackBothChannels(bool outputFails)
    {
        var micRunning = false;
        var outputRunning = false;
        var rollbacks = 0;
        Assert.ThrowsAny<Exception>(() => CaptureStartup.Start(
            () => micRunning = true,
            () => { outputRunning = true; if (outputFails) throw new IOException("Output disappeared"); },
            () => false,
            () => { micRunning = false; outputRunning = false; rollbacks++; }));
        Assert.False(micRunning);
        Assert.False(outputRunning);
        Assert.Equal(1, rollbacks);
    }

    [Fact]
    public void FailedMicrophonePreventsOutputStartupAndRollsBackResources()
    {
        var outputStarted = false;
        var cleaned = false;
        Assert.Throws<IOException>(() => CaptureStartup.Start(
            () => throw new IOException("Microphone unavailable"),
            () => outputStarted = true, () => true, () => cleaned = true));
        Assert.False(outputStarted);
        Assert.True(cleaned);
    }

    [Fact]
    public void SilentOutputCanStartWhenMicrophoneProvidesCallbacks()
    {
        var outputStarted = false;
        CaptureStartup.Start(() => { }, () => outputStarted = true, () => true,
            () => throw new Exception("Successful startup must not roll back"));
        Assert.True(outputStarted);
    }
}
