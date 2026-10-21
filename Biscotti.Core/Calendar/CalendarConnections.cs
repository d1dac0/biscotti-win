using System.Security.Cryptography;
using System.Text.Json;
using Biscotti.Core.Storage;

namespace Biscotti.Core.Calendar;

/// <summary>Private provider URLs/passwords must never be displayed or logged via ToString.</summary>
public sealed record CalendarConnection(string Id, string Provider, string Label,
    string? FeedUrl = null, string? AppleAccount = null, string? ApplePassword = null)
{
    public override string ToString() => $"{Provider} calendar connection";
}

public static class CalendarProviders
{
    public static ICalendarSource Create(CalendarConnection connection) => connection.Provider switch
    {
        "google" or "microsoft" => new CalendarFeedSource(connection),
        "icloud" => new ICloudCalendarSource(connection),
        _ => throw new InvalidDataException("Unsupported calendar provider.")
    };
}

public sealed class CalendarConnectionStore
{
    private readonly object _gate = new();
    private readonly string _path;
    public CalendarConnectionStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BiscottiWin", "Calendar", "connections.dpapi");

    public List<CalendarConnection> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return [];
            var bytes = ProtectedMeetingText.ReadAndMigrate(_path);
            try { return JsonSerializer.Deserialize<List<CalendarConnection>>(bytes) ?? []; }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
    }

    public void Save(IReadOnlyList<CalendarConnection> connections)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(connections);
            try { ProtectedMeetingText.WriteProtectedAtomically(_path, bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
    }
}

public sealed class CompositeCalendarSource(IReadOnlyList<(string Label, ICalendarSource Source)> sources) : ICalendarSource
{
    public async Task<CalendarSchedule> ReadAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default)
    {
        var results = await Task.WhenAll(sources.Select(async source =>
        {
            try { return await source.Source.ReadAsync(from, until, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return new CalendarSchedule([], [], [$"{source.Label} could not refresh. Open Calendars to reconnect."]); }
        }));
        return new(results.SelectMany(r => r.Calendars).ToArray(), results.SelectMany(r => r.Events).ToArray(),
            results.SelectMany(r => r.Warnings ?? []).ToArray());
    }
}
