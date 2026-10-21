using Biscotti.Core;
using Biscotti.Core.Audio;
using Xunit;

namespace Biscotti.Tests;

public sealed class AudioDeviceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrDisconnectedSelectionUsesCurrentDefaultOnRepeatedStarts(bool savedSelection)
    {
        var settings = savedSelection ? Selection() : new AppSettings();
        settings.MicDeviceEndpointId = savedSelection ? "disconnected" : null;
        var defaultMic = new WaveInDeviceInfo(2, "Windows input", "default-input");
        var devices = Snapshot() with
        {
            Microphones = [new(0, "Webcam"), defaultMic], DefaultMicrophone = defaultMic
        };
        for (var i = 0; i < 2; i++)
        {
            AudioDevices.PrepareForRecording(settings, devices);
            Assert.Equal(defaultMic, AudioDevices.ResolveMicrophone(settings, devices));
            Assert.Equal(2, settings.MicDeviceIndex);
            Assert.Equal("Windows input", settings.MicDeviceName);
            Assert.Equal("default-input", settings.MicDeviceEndpointId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConnectedExplicitMicrophoneWinsOverDefaultEvenAfterIndexChange(bool endpointKnown)
    {
        var settings = Selection();
        settings.MicDeviceEndpointId = endpointKnown ? "headset" : null;
        var selected = new WaveInDeviceInfo(endpointKnown ? 3 : 1, "Headset", "headset");
        var defaultMic = new WaveInDeviceInfo(0, "Webcam", "webcam");
        var devices = Snapshot() with
        {
            Microphones = [defaultMic, selected], DefaultMicrophone = defaultMic
        };
        AudioDevices.PrepareForRecording(settings, devices);
        Assert.Equal(selected.Index, settings.MicDeviceIndex);
        Assert.Equal("headset", settings.MicDeviceEndpointId);
    }

    [Fact]
    public void NoSelectionAndNoWindowsDefaultStillRequiresAChoice()
    {
        Assert.Throws<AudioDeviceException>(() => AudioDevices.PrepareForRecording(new(), Snapshot()));
        Assert.Throws<AudioDeviceException>(() => AudioDevices.PrepareForRecording(Selection(),
            Snapshot() with { Microphones = [] }));
    }

    [Fact]
    public void DefaultInputUsesMatchedDeviceOrWindowsMapperInsteadOfFirstMicrophone()
    {
        var matched = new WaveInDeviceInfo(2, "Headset", "default-input");
        Assert.Equal(matched, AudioDevices.FindDefaultMicrophone([new(0, "Webcam"), matched], "default-input"));
        var mapper = AudioDevices.FindDefaultMicrophone([new(0, "Webcam")], "unmatched-default");
        Assert.Equal(-1, mapper.Index);
        Assert.Equal("unmatched-default", mapper.EndpointId);
        var settings = new AppSettings();
        AudioDevices.PrepareForRecording(settings, Snapshot() with
        {
            Microphones = [new(0, "Webcam"), mapper], DefaultMicrophone = mapper
        });
        Assert.Equal(-1, settings.MicDeviceIndex);
    }

    [Fact]
    public void MicrophoneRecoveryDoesNotOverrideMissingExplicitOutput()
    {
        var microphone = new WaveInDeviceInfo(0, "Webcam");
        Assert.Throws<AudioDeviceException>(() => AudioDevices.PrepareForRecording(Selection("missing"),
            Snapshot() with { DefaultMicrophone = microphone }));
    }

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
        Assert.All(MicrophoneEndpointMatcher.Attach(microphones, []), m => Assert.Null(m.EndpointId));

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
