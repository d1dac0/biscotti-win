using Biscotti.Core.Audio;

namespace Biscotti.Tests;

public class PerAppLoopbackTests
{
    [Theory]
    [InlineData(22000, 123, true)]
    [InlineData(21999, 123, false)]
    [InlineData(22631, 0, false)]
    public void ProcessCaptureRequiresWindows11ASelectedProcessAndNoSystemWideConfirmation(
        int build, int processId, bool expected)
    {
        Assert.Equal(expected, LoopbackCapturePolicy.ShouldUseProcessLoopback(build, processId));
    }

    [Fact]
    public void ExplicitSystemWideRetrySkipsProcessCapture()
    {
        Assert.False(LoopbackCapturePolicy.ShouldUseProcessLoopback(22631, 123, forceSystemWide: true));
    }

    [Fact]
    public void FailedProcessCaptureIsDisposedAndRequestsConsentBeforeDeviceFallback()
    {
        var disposedProcess = false;
        var deviceCreated = false;

        var error = Assert.Throws<SystemWideCaptureConsentException>(() => LoopbackCaptureStartup.StartWithFallback(
            attemptProcess: true, allowDeviceFallback: false,
            createProcess: () => "process", configureProcess: _ => { },
            startProcess: _ => throw new InvalidOperationException("activation denied"),
            disposeProcess: _ => disposedProcess = true,
            createDevice: () => { deviceCreated = true; return "device"; },
            configureDevice: _ => { }, startDevice: _ => { }, disposeDevice: _ => { }, onFallback: _ => { }));

        Assert.True(disposedProcess);
        Assert.False(deviceCreated);
        Assert.Contains("activation denied", error.Message);
        Assert.Contains("all speaker audio", error.Message);
    }

    [Fact]
    public void ConfirmedRetryStartsDeviceLoopbackAfterDisposingFailedProcessCapture()
    {
        var disposedProcess = false;
        var fallbackMessage = string.Empty;

        var result = LoopbackCaptureStartup.StartWithFallback(
            attemptProcess: false, allowDeviceFallback: true,
            createProcess: () => "process", configureProcess: _ => { }, startProcess: _ => { },
            disposeProcess: _ => disposedProcess = true,
            createDevice: () => "device", configureDevice: _ => { }, startDevice: _ => { },
            disposeDevice: _ => { }, onFallback: message => fallbackMessage = message);

        Assert.Equal("device", result);
        Assert.False(disposedProcess);
        Assert.Equal(string.Empty, fallbackMessage);
    }

    [Fact]
    public void UnsupportedWindowsRequiresConsentForDeviceLoopback()
    {
        var deviceCreated = false;

        Assert.Throws<SystemWideCaptureConsentException>(() => LoopbackCaptureStartup.StartWithFallback(
            attemptProcess: false, allowDeviceFallback: false,
            createProcess: () => "process", configureProcess: _ => { }, startProcess: _ => { },
            disposeProcess: _ => { }, createDevice: () => { deviceCreated = true; return "device"; },
            configureDevice: _ => { }, startDevice: _ => { }, disposeDevice: _ => { }, onFallback: _ => { }));

        Assert.False(deviceCreated);
    }

    [Fact]
    public void ConfirmedDeviceLoopbackFailureDisposesTheDeviceRecorder()
    {
        var disposedDevice = false;

        Assert.Throws<InvalidOperationException>(() => LoopbackCaptureStartup.StartWithFallback(
            attemptProcess: false, allowDeviceFallback: true,
            createProcess: () => "process", configureProcess: _ => { }, startProcess: _ => { },
            disposeProcess: _ => { }, createDevice: () => "device", configureDevice: _ => { },
            startDevice: _ => throw new InvalidOperationException("device unavailable"),
            disposeDevice: _ => disposedDevice = true, onFallback: _ => { }));

        Assert.True(disposedDevice);
    }

    [Fact]
    public void NoProcessSelectionStartsDeviceLoopbackWithoutFallbackWarning()
    {
        var processCreated = false;
        var fallbackReported = false;

        var result = LoopbackCaptureStartup.StartWithFallback(
            attemptProcess: false, allowDeviceFallback: true,
            createProcess: () => { processCreated = true; return "process"; },
            configureProcess: _ => { },
            startProcess: _ => { },
            disposeProcess: _ => { },
            createDevice: () => "device",
            configureDevice: _ => { },
            startDevice: _ => { },
            disposeDevice: _ => { },
            onFallback: _ => fallbackReported = true);

        Assert.Equal("device", result);
        Assert.False(processCreated);
        Assert.False(fallbackReported);
    }
}
