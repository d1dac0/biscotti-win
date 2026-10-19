using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Biscotti.Core.Models;

namespace Biscotti.Core.Storage;

/// <summary>
/// Meetings live under %LOCALAPPDATA%\BiscottiWin\Meetings\<id>\:
///   meeting.json — structured data (transcript, summary, notes)
///   meeting.md   — human-readable copy you can open in any editor
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

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BiscottiWin", "Meetings");

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
        File.WriteAllText(Path.Combine(dir, "meeting.json"), JsonSerializer.Serialize(m, JsonOpts));
        File.WriteAllText(Path.Combine(dir, "meeting.md"), RenderMarkdown(m), new UTF8Encoding(false));
    }

    public static List<Meeting> LoadAll()
    {
        var result = new List<Meeting>();
        if (!Directory.Exists(Root)) return result;
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var json = Path.Combine(dir, "meeting.json");
            if (!File.Exists(json)) continue;
            try
            {
                result.Add(JsonSerializer.Deserialize<Meeting>(File.ReadAllText(json), JsonOpts)!);
            }
            catch { /* skip corrupt meeting */ }
        }
        return result.OrderByDescending(m => m.StartedUtc).ToList();
    }

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
        foreach (var l in m.Transcript.OrderBy(x => x.StartSeconds))
            sb.AppendLine($"- [{TimeSpan.FromSeconds(l.StartSeconds):hh\\:mm\\:ss}] **{l.Speaker}**: {l.Text.Trim()}");
        return sb.ToString();
    }
}
