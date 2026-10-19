using Biscotti.Core.Audio;

namespace Biscotti.Tests;

public class PerAppLoopbackTests
{
    [Theory]
    [InlineData(22000, 123, true)]
    [InlineData(21999, 123, false)]
    [InlineData(22631, 0, false)]
    public void ProcessCaptureRequiresWindows11AndASelectedProcess(int build, int processId, bool expected)
    {
        Assert.Equal(expected, LoopbackCapturePolicy.ShouldUseProcessLoopback(build, processId));
    }

    [Fact]
    public void FailedProcessCaptureIsDisposedAndFallsBackToDevice()
    {
        var disposedProcess = false;
        string? fallbackMessage = null;

        var result = LoopbackCaptureStartup.StartWithFallback(
            attemptProcess: true,
            createProcess: () => "process",
            configureProcess: _ => { },
            startProcess: _ => throw new InvalidOperationException("activation denied"),
            disposeProcess: _ => disposedProcess = true,
            createDevice: () => "device",
            configureDevice: _ => { },
            startDevice: _ => { },
            disposeDevice: _ => { },
            onFallback: message => fallbackMessage = message);

        Assert.Equal("device", result);
        Assert.True(disposedProcess);
        Assert.Equal("activation denied", fallbackMessage);
    }

    [Fact]
    public void NoProcessSelectionStartsDeviceLoopbackWithoutFallbackWarning()
    {
        var processCreated = false;
        var fallbackReported = false;

        var result = LoopbackCaptureStartup.StartWithFallback(
            attemptProcess: false,
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
