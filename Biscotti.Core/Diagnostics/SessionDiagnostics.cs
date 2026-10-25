using System.Text;
using System.Text.Json;

namespace Biscotti.Core.Diagnostics;

/// <summary>
/// Always-on, crash-first local diagnostics trail under %LOCALAPPDATA%\BiscottiWin\logs.
/// Issue #126: the app vanished mid-recording leaving no OS crash record at all, so the
/// next occurrence must be attributable from local evidence alone. Every member is
/// defensive — diagnostics must never take the app down, so all failures are swallowed.
///
/// The on-disk state distinguishes how the previous session died:
///  - marker present at next start  → ProcessExit never ran (hard kill, power loss, or a
///    native crash the CLR could not run down; check Windows Error Reporting for the latter).
///  - crash-flag.json present        → a managed exception hook fired before death
///    (full detail is in that session's log).
///  - neither, log ends with an exit-code line → clean or exit()-style rundown.
/// Sessions rotate: the newest ten session-*.log files are kept.
/// </summary>
public static class SessionDiagnostics
{
    private const int KeepSessions = 10;
    private const int MaxMessageLength = 8 * 1024;

    private static readonly object Gate = new();
    private static string? _sessionFile;
    private static int _sessionSequence;

    public static string Root => RootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BiscottiWin", "logs");

    /// <summary>Test hook; null in the real app. Tests point Root at a temp dir.</summary>
    internal static string? RootOverride;

    private static string MarkerPath => Path.Combine(Root, "session-active.json");
    private static string CrashFlagPath => Path.Combine(Root, "crash-flag.json");

    /// <summary>
    /// Begins a session: rotates old logs, inspects the previous session's end state and
    /// returns a one-line summary of it (null when the previous session ended cleanly
    /// or none ran). The summary is also written to the new session's log.
    /// </summary>
    public static string? Start()
    {
        lock (Gate)
        {
            string? previous = null;
            try
            {
                Directory.CreateDirectory(Root);
                previous = DescribePreviousEnd();
                PruneOldSessions();
            }
            catch { return null; }

            _sessionFile = Path.Combine(Root,
                $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{System.Threading.Interlocked.Increment(ref _sessionSequence)}.log");

            try
            {
                WriteRaw($"INFO app: session started pid={Environment.ProcessId} " +
                         $"utc={DateTime.UtcNow:O} os={Environment.OSVersion.VersionString} " +
                         $"clr={Environment.Version}");
                if (previous is not null)
                    WriteRaw("WARNING app: " + previous);
            }
            catch { _sessionFile = null; return null; }

            try
            {
                File.WriteAllText(MarkerPath, JsonSerializer.Serialize(new
                {
                    startedUtc = DateTime.UtcNow,
                    pid = Environment.ProcessId
                }));
            }
            catch { }

            return previous;
        }
    }

    /// <summary>Called from AppDomain.ProcessExit — the only end path that runs for both
    /// clean shutdowns and native exit(); it does not run for TerminateProcess-style kills.</summary>
    public static void EndProcess(int exitCode)
    {
        lock (Gate)
        {
            if (_sessionFile is null) return;
            try { WriteRaw($"INFO app: process exit code={exitCode} (ProcessExit ran)"); } catch { }
            try { File.Delete(MarkerPath); } catch { }
            _sessionFile = null;
        }
    }

    public static void Info(string category, string message) => Write("INFO", category, message);
    public static void Warning(string category, string message) => Write("WARNING", category, message);
    public static void Error(string category, string message) => Write("ERROR", category, message);

    /// <summary>Logs a managed exception with its full chain, and leaves crash-flag.json
    /// behind so the next start can report the crash even though the process died.</summary>
    public static void Crash(string source, Exception exception)
    {
        lock (Gate)
        {
            try { WriteRaw($"ERROR crash: {source}: {Flatten(exception)}"); } catch { }
            try
            {
                File.WriteAllText(CrashFlagPath, JsonSerializer.Serialize(new
                {
                    utc = DateTime.UtcNow,
                    source,
                    detail = exception.Message
                }));
            }
            catch { }
        }
    }

    /// <summary>Logs an exception without the crash flag (non-fatal paths).</summary>
    public static void Exception(string source, Exception exception) =>
        Write("ERROR", source, Flatten(exception));

    private static void Write(string level, string category, string message)
    {
        lock (Gate)
        {
            if (_sessionFile is null) return;
            try { WriteRaw($"{level} {category}: {message}"); } catch { }
        }
    }

    private static void WriteRaw(string line)
    {
        if (_sessionFile is null) return;
        var capped = line.Length > MaxMessageLength
            ? line[..MaxMessageLength] + $"…[truncated {line.Length - MaxMessageLength} chars]"
            : line;
        File.AppendAllText(_sessionFile,
            $"[{DateTime.Now:HH:mm:ss.fff}] {capped}{Environment.NewLine}", Encoding.UTF8);
    }

    private static string? DescribePreviousEnd()
    {
        if (File.Exists(CrashFlagPath))
        {
            string detail = "";
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(CrashFlagPath));
                if (doc.RootElement.TryGetProperty("source", out var source))
                    detail = $" ({source.GetString()})";
            }
            catch { }
            try { File.Delete(CrashFlagPath); } catch { }
            return $"previous session logged a fatal managed exception{detail}; see the previous session log";
        }

        if (File.Exists(MarkerPath))
        {
            try { File.Delete(MarkerPath); } catch { }
            return "previous session ended without a process-exit record " +
                   "(hard kill, power loss, or a native crash; check Windows Error Reporting)";
        }

        return null;
    }

    private static void PruneOldSessions()
    {
        var logs = new DirectoryInfo(Root)
            .GetFiles("session-*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(KeepSessions - 1);  // leave room for the session about to start
        foreach (var stale in logs)
        {
            try { stale.Delete(); } catch { }
        }
    }

    private static string Flatten(Exception exception)
    {
        var builder = new StringBuilder();
        for (var ex = exception; ex is not null; ex = ex.InnerException)
            builder.Append(builder.Length == 0 ? "" : " <- ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        return builder.ToString().ReplaceLineEndings(" | ");
    }
}
