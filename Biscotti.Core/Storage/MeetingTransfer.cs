using System.Globalization;
using System.Text;
using System.Text.Json;
using Biscotti.Core.Models;

namespace Biscotti.Core.Storage;

public sealed record ImportPreview(IReadOnlyList<Meeting> Meetings, int DuplicateRows,
    IReadOnlyList<string> Errors)
{
    public int InvalidRows => Errors.Count;
    public string Counts => $"{Meetings.Count} ready to import · {DuplicateRows} duplicates skipped · {InvalidRows} invalid rows";
}

public sealed record ImportResult(int Imported, int Duplicates, bool Cancelled, string? Error);

/// <summary>Explicit plaintext interchange; normal storage remains DPAPI-protected.</summary>
public static class MeetingTransfer
{
    public const int MaxCsvBytes = 32 * 1024 * 1024;
    private static readonly string[] Columns = ["Version", "Id", "StartedUtc", "EndedUtc", "Title",
        "Summary", "Notes", "CalendarAttendees", "ActionItems", "Transcript", "TimestampedNotes"];

    public static string RenderCsv(IEnumerable<Meeting> meetings, CancellationToken token = default)
    {
        var output = new StringBuilder();
        WriteRow(output, Columns);
        foreach (var m in meetings)
        {
            token.ThrowIfCancellationRequested();
            WriteRow(output, ["1", m.Id.ToString(), m.StartedUtc.ToUniversalTime().ToString("O"),
                m.EndedUtc?.ToUniversalTime().ToString("O") ?? "", m.Title, m.Summary ?? "", m.Notes ?? "",
                JsonSerializer.Serialize(m.CalendarAttendees), JsonSerializer.Serialize(m.ActionItems),
                JsonSerializer.Serialize(m.Transcript), JsonSerializer.Serialize(m.TimestampedNotes)]);
        }
        return output.ToString();
    }

    private static void WriteRow(StringBuilder output, IEnumerable<string> cells) =>
        output.AppendJoin(',', cells.Select(cell => '"' + cell.Replace("\"", "\"\"") + '"')).Append("\r\n");

    public static ImportPreview PreviewFile(string path, IEnumerable<Guid> existingIds, CancellationToken token = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxCsvBytes) throw new InvalidDataException("CSV exceeds the 32 MiB import limit.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        return PreviewCsv(reader.ReadToEnd(), existingIds, token);
    }

    public static ImportPreview PreviewCsv(string csv, IEnumerable<Guid> existingIds, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(csv) > MaxCsvBytes)
            throw new InvalidDataException("CSV exceeds the 32 MiB import limit.");
        using var rows = ReadRows(csv.TrimStart('\uFEFF'), token).GetEnumerator();
            if (!rows.MoveNext() || !rows.Current.SequenceEqual(Columns))
                throw new InvalidDataException("Use a Biscotti Windows CSV v1 file. The header does not match; see docs/local-meeting-transfer.md.");
            var seen = existingIds.ToHashSet();
            var meetings = new List<Meeting>();
            var errors = new List<string>();
            var duplicates = 0;
            var row = 1;
            while (rows.MoveNext())
            {
                token.ThrowIfCancellationRequested();
                if (++row > 10001) throw new InvalidDataException("CSV exceeds the 10,000 meeting limit.");
                var cells = rows.Current;
                try
                {
                    var m = ParseMeeting(cells);
                    if (!seen.Add(m.Id)) duplicates++;
                    else meetings.Add(m);
                }
                catch (Exception ex) when (ex is InvalidDataException or JsonException or FormatException)
                {
                    // Do not echo private row content in diagnostics.
                    errors.Add($"Row {row}: invalid fields, dates or timeline. Check the CSV format.");
                }
            }
            return new(meetings.AsReadOnly(), duplicates, errors.AsReadOnly());
    }

    // Preserve every character inside quotes, including blank lines and CRLF.
    // TextFieldParser drops blank quoted lines, so it cannot provide a lossless transfer.
    private static IEnumerable<string[]> ReadRows(string csv, CancellationToken token)
    {
        var cells = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var closedQuote = false;
        for (var i = 0; i < csv.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            var c = csv[i];
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                else { quoted = false; closedQuote = true; }
                continue;
            }
            if (c is ',' or '\r' or '\n')
            {
                cells.Add(field.ToString());
                field.Clear();
                closedQuote = false;
                if (c == ',') continue;
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                yield return cells.ToArray();
                cells.Clear();
            }
            else if (c == '"' && field.Length == 0 && !closedQuote) quoted = true;
            else if (c == '"' || closedQuote) throw new InvalidDataException("Malformed CSV quoting. No meetings were imported.");
            else field.Append(c);
        }
        if (quoted) throw new InvalidDataException("Unclosed CSV quote. No meetings were imported.");
        if (field.Length > 0 || closedQuote || cells.Count > 0)
        {
            cells.Add(field.ToString());
            yield return cells.ToArray();
        }
    }

    private static Meeting ParseMeeting(string[] c)
    {
        if (c.Length != Columns.Length || c[0] != "1" || !Guid.TryParse(c[1], out var id) || id == Guid.Empty)
            throw new InvalidDataException();
        var started = ParseUtc(c[2]);
        DateTime? ended = c[3].Length == 0 ? null : ParseUtc(c[3]);
        if (string.IsNullOrWhiteSpace(c[4]) || ended < started) throw new InvalidDataException();
        var attendees = ParseList<string>(c[7]);
        var actions = ParseList<string>(c[8]);
        var transcript = ParseList<TranscriptLine>(c[9]);
        var notes = ParseList<TimestampedMeetingNote>(c[10]);
        if (transcript.Any(line => line.Text is null || line.Speaker is null || !ValidSeconds(line.StartSeconds)
            || !ValidSeconds(line.EndSeconds) || (line.EndSeconds != 0 && line.EndSeconds < line.StartSeconds))
            || notes.Any(note => note.Text is null || !ValidSeconds(note.StartSeconds)))
            throw new InvalidDataException();
        return new Meeting
        {
            Id = id, StartedUtc = started, EndedUtc = ended, Title = c[4], Summary = c[5], Notes = c[6],
            CalendarAttendees = attendees, ActionItems = actions, Transcript = transcript, TimestampedNotes = notes,
        };
    }

    private static DateTime ParseUtc(string value)
    {
        // Require an explicit offset; local machine timezone must never reinterpret a transfer.
        string[] formats = ["O", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.fffK"];
        if (!(value.EndsWith('Z') || (value.Length >= 6 && (value[^6] == '+' || value[^6] == '-') && value[^3] == ':'))
            || !DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new InvalidDataException();
        return date.UtcDateTime;
    }

    private static List<T> ParseList<T>(string json) where T : class
    {
        var list = JsonSerializer.Deserialize<List<T>>(json);
        if (list is null || list.Any(item => item is null)) throw new InvalidDataException();
        return list;
    }

    private static bool ValidSeconds(double seconds) => double.IsFinite(seconds) && seconds >= 0 && seconds <= 3_600_000;

    public static ImportResult Import(ImportPreview preview, CancellationToken token = default, IProgress<int>? progress = null)
    {
        var imported = 0;
        var duplicates = preview.DuplicateRows;
        foreach (var meeting in preview.Meetings)
        {
            if (token.IsCancellationRequested) return new(imported, duplicates, true, null);
            try
            {
                // Check again at commit: another session may have imported this ID after preview.
                if (Directory.Exists(MeetingStore.MeetingDir(meeting.Id))) { duplicates++; continue; }
                MeetingStore.SaveNew(meeting);
                imported++;
                progress?.Report(imported);
            }
            catch (IOException) when (Directory.Exists(MeetingStore.MeetingDir(meeting.Id))) { duplicates++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                return new(imported, duplicates, false, "Could not protect or save a meeting. Check the Windows profile and storage permissions, then retry; already imported IDs will be skipped.");
            }
        }
        return new(imported, duplicates, token.IsCancellationRequested, null);
    }

    /// <summary>Stage outside internal storage; cancellation leaves the destination intact.</summary>
    public static void ExportText(string path, string text, CancellationToken token = default)
    {
        var destination = Path.GetFullPath(path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(MeetingStore.Root));
        if (destination.Equals(root, StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a folder outside Biscotti's protected meeting storage.");
        // Reject reparse-point ancestors so a folder alias cannot bypass the storage boundary.
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(destination)!); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Choose a direct folder rather than a folder link for export.");
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".biscotti-export-{Guid.NewGuid():N}.tmp");
        token.ThrowIfCancellationRequested();
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(true));
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
