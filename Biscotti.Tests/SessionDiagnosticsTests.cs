using Biscotti.Core.Diagnostics;
using Xunit;

namespace Biscotti.Tests;

/// <summary>
/// Issue #126: the app vanished mid-recording leaving no crash record anywhere. The
/// session diagnostics trail must distinguish how the previous session ended and must
/// never throw, whatever shape the logs directory is in.
/// </summary>
[Collection("MeetingStore")]
public class SessionDiagnosticsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"diagnostics-test-{Guid.NewGuid():N}");

    public SessionDiagnosticsTests()
    {
        Directory.CreateDirectory(_dir);
        SessionDiagnostics.RootOverride = _dir;
    }

    public void Dispose()
    {
        SessionDiagnostics.RootOverride = null;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void StartReportsNothingWhenNoPreviousSessionRan()
    {
        Assert.Null(SessionDiagnostics.Start());
        Assert.True(File.Exists(Path.Combine(_dir, "session-active.json")));
    }

    [Fact]
    public void CleanEndLeavesNoMarkerAndNextStartIsQuiet()
    {
        SessionDiagnostics.Start();
        SessionDiagnostics.Info("test", "hello");
        var first = NewestSessionLog();

        SessionDiagnostics.EndProcess(0);
        Assert.False(File.Exists(Path.Combine(_dir, "session-active.json")));

        Assert.Null(SessionDiagnostics.Start());
        var firstLog = File.ReadAllText(first);
        Assert.Contains("process exit code=0", firstLog);
        Assert.Contains("hello", firstLog);
    }

    [Fact]
    public void MissingProcessExitIsReportedAsUncleanEnd()
    {
        SessionDiagnostics.Start();
        // Simulate a death with no ProcessExit: the marker is left dangling.
        var previous = NewestSessionLog();

        var summary = SessionDiagnostics.Start();
        Assert.NotNull(summary);
        Assert.Contains("without a process-exit record", summary);
        Assert.Contains("WARNING app", File.ReadAllText(NewestSessionLog()));
        Assert.NotEqual(previous, NewestSessionLog());
    }

    [Fact]
    public void CrashFlagIsReportedAndConsumedOnce()
    {
        SessionDiagnostics.Start();
        SessionDiagnostics.Crash("unit-test-source",
            new InvalidOperationException("boom", new ArgumentException("inner")));

        var summary = SessionDiagnostics.Start();
        Assert.NotNull(summary);
        Assert.Contains("unit-test-source", summary);
        // The session that read the flag ends cleanly; the session after it is quiet.
        SessionDiagnostics.EndProcess(0);
        Assert.Null(SessionDiagnostics.Start());
    }

    [Fact]
    public void CrashLogsTheFullExceptionChain()
    {
        SessionDiagnostics.Start();
        SessionDiagnostics.Crash("unit-test-source",
            new InvalidOperationException("boom", new ArgumentException("inner")));

        var log = File.ReadAllText(NewestSessionLog());
        Assert.Contains("InvalidOperationException: boom", log);
        Assert.Contains("ArgumentException: inner", log);
    }

    [Fact]
    public void WritesAfterEndProcessAreDropped()
    {
        SessionDiagnostics.Start();
        var sessionFile = NewestSessionLog();
        SessionDiagnostics.EndProcess(0);

        SessionDiagnostics.Info("test", "after end");

        var log = File.ReadAllText(sessionFile);
        Assert.DoesNotContain("after end", log);
    }

    [Fact]
    public void OldSessionsAreRotatedKeepingTheNewestTen()
    {
        for (var i = 0; i < 12; i++)
        {
            var stale = Path.Combine(_dir, $"session-20260101-0000{i:00}-1.log");
            File.WriteAllText(stale, "stale");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-i));
        }

        SessionDiagnostics.Start();

        // 9 of the 12 pre-existing logs survive, plus the fresh one = 10.
        Assert.Equal(10, Directory.GetFiles(_dir, "session-*.log").Length);
        Assert.Contains("session started", File.ReadAllText(NewestSessionLog()));
    }

    [Fact]
    public void UnwritableRootNeverThrowsAndStaysSilent()
    {
        // Point the root at a path occupied by a file: directory creation fails.
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "");
        SessionDiagnostics.RootOverride = Path.Combine(blocker, "logs");

        Assert.Null(SessionDiagnostics.Start());
        SessionDiagnostics.Info("test", "dropped");
        SessionDiagnostics.EndProcess(0);
    }

    private string NewestSessionLog() =>
        new DirectoryInfo(_dir)
            .GetFiles("session-*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .First()
            .FullName;
}
