using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Biscotti.Core.Audio;
using Biscotti.Core.Models;

namespace Biscotti.Core.Storage;

/// <summary>
/// Meetings live under %LOCALAPPDATA%\BiscottiWin\Meetings\<id>\:
///   meeting.json — structured data (transcript, summary, notes)
///   meeting.md   — protected, human-readable Markdown copy
///   mic.wav / sys.wav — full audio, kept unless the user deletes the meeting
/// </summary>
public static class MeetingStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string Root => RootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BiscottiWin", "Meetings");

    /// <summary>Test hook; null in the real app. Tests point Root at a temp dir.</summary>
    internal static string? RootOverride;

    public static string MeetingDir(Guid id) => Path.Combine(Root, id.ToString());

    public static MeetingDirResult CreateNew()
    {
        var meeting = new Meeting();
        Directory.CreateDirectory(MeetingDir(meeting.Id));
        return new MeetingDirResult(meeting, MeetingDir(meeting.Id));
    }

    public record MeetingDirResult(Meeting Meeting, string Dir);

    public static void Save(Meeting m)
    {
        var dir = MeetingDir(m.Id);
        Directory.CreateDirectory(dir);
        SaveToDirectory(m, dir);
    }

    /// <summary>Publish a fully protected import without replacing an existing directory.</summary>
    internal static void SaveNew(Meeting m)
    {
        Directory.CreateDirectory(Root);
        var staging = Path.Combine(Root, $".import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            SaveToDirectory(m, staging);
            Directory.Move(staging, MeetingDir(m.Id));
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static void SaveToDirectory(Meeting m, string dir)
    {
        var jsonPath = Path.Combine(dir, "meeting.json");
        var json = JsonSerializer.SerializeToUtf8Bytes(m, JsonOpts);
        try { ProtectedMeetingText.WriteProtectedAtomically(jsonPath, json); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(json); }

        var mdPath = Path.Combine(dir, "meeting.md");
        var markdown = new UTF8Encoding(false).GetBytes(RenderMarkdown(m));
        try { ProtectedMeetingText.WriteProtectedAtomically(mdPath, markdown); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(markdown); }
    }

    public static List<Meeting> LoadAll()
    {
        var records = ReadMeetingMetadata();
        foreach (var record in records)
            MeetingAudioMigration.EnsureEncrypted(record.Directory);
        return records.Select(record => record.Meeting).ToList();
    }

    /// <summary>Reads meeting.json files without opening or migrating audio.</summary>
    public static List<Meeting> LoadMetadataOnly() =>
        ReadMeetingMetadata().Select(record => record.Meeting).ToList();

    private static List<(string Directory, Meeting Meeting)> ReadMeetingMetadata()
    {
        var result = new List<(string Directory, Meeting Meeting)>();
        string[] directories;
        try
        {
            if (!TryGetDirectory(Root)) return result;
            directories = Directory.GetDirectories(Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw MeetingTextStorageException.For(Root, "list meeting directories in", ex);
        }

        foreach (var dir in directories)
        {
            if (Path.GetFileName(dir).StartsWith(".import-", StringComparison.Ordinal)) continue;
            var json = Path.Combine(dir, "meeting.json");
            var markdown = Path.Combine(dir, "meeting.md");
            var hasJson = TryGetFile(json);
            var hasMarkdown = TryGetFile(markdown);
            if (!hasJson && !hasMarkdown) continue;
            if (!hasJson)
                throw MeetingTextStorageException.For(json, "read", new FileNotFoundException(
                    "The meeting has a Markdown copy but no structured meeting.json file."));

            var bytes = ProtectedMeetingText.ReadAndMigrate(json, ValidateLegacyJson);
            Meeting meeting;
            try
            {
                meeting = JsonSerializer.Deserialize<Meeting>(bytes, JsonOpts)
                    ?? throw new InvalidDataException("The meeting JSON contained no meeting record.");
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                throw MeetingTextStorageException.For(json, "read", ex);
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }

            result.Add((dir, meeting));

            if (hasMarkdown)
            {
                var markdownBytes = ProtectedMeetingText.ReadAndMigrate(markdown);
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(markdownBytes);
            }
        }
        return result.OrderByDescending(record => record.Meeting.StartedUtc).ToList();
    }

    private static bool TryGetDirectory(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw MeetingTextStorageException.For(path, "inspect meeting storage", ex);
        }
        if ((attributes & FileAttributes.Directory) == 0)
            throw MeetingTextStorageException.For(path, "inspect meeting storage",
                new InvalidDataException("The meeting storage path is not a directory."));
        return true;
    }

    private static bool TryGetFile(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw MeetingTextStorageException.For(path, "inspect", ex);
        }
        if ((attributes & FileAttributes.Directory) != 0)
            throw MeetingTextStorageException.For(path, "inspect",
                new InvalidDataException("A directory exists where a meeting data file was expected."));
        return true;
    }

    private static void ValidateLegacyJson(byte[] bytes)
    {
        var meeting = JsonSerializer.Deserialize<Meeting>(bytes, JsonOpts);
        if (meeting is null)
            throw new InvalidDataException("The meeting JSON contained no meeting record.");
    }

    public static Task<List<Meeting>> LoadAllAsync() => Task.Run(LoadAll);

    public static void Delete(Guid id)
    {
        var dir = MeetingDir(id);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    public static string RenderMarkdown(Meeting m)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {m.Title}");
        sb.AppendLine();
        sb.AppendLine($"_{m.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm}_ — {m.Transcript.Count} lines");
        if (m.Tags.Count > 0) sb.AppendLine($"**Tags:** {string.Join(", ", m.Tags)}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(m.Summary))
        {
            sb.AppendLine(m.Summary);
            sb.AppendLine();
        }
        if (m.ActionItems.Count > 0)
        {
            sb.AppendLine("## Action Items");
            foreach (var a in m.ActionItems) sb.AppendLine($"- {a}");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(m.Notes))
        {
            sb.AppendLine("## My Notes");
            sb.AppendLine(m.Notes);
            sb.AppendLine();
        }
        sb.AppendLine("## Transcript");
        var timeline = m.Transcript
            .Select(line => (line.StartSeconds, Markdown: $"- [{TimeSpan.FromSeconds(line.StartSeconds):hh\\:mm\\:ss}] **{line.Speaker}**: {line.Text.Trim()}"))
            .Concat(m.TimestampedNotes.Select(note =>
                (note.StartSeconds, Markdown: $"- [{TimeSpan.FromSeconds(note.StartSeconds):hh\\:mm\\:ss}] **Note**: {note.Text.Trim()}")))
            .OrderBy(entry => entry.StartSeconds);
        foreach (var entry in timeline)
            sb.AppendLine(entry.Markdown);
        return sb.ToString();
    }
}
