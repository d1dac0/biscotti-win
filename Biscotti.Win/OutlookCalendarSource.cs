using System.Globalization;
using System.Runtime.InteropServices;
using Biscotti.Core.Calendar;

namespace Biscotti.Win;

/// <summary>Read-only classic Outlook access. COM objects remain on a background STA.</summary>
internal sealed class OutlookCalendarSource : ICalendarSource
{
    private readonly object _gate = new();
    private Task<CalendarSchedule>? _pending;
    private DateTimeOffset _pendingFrom, _pendingUntil;

    public async Task<CalendarSchedule> ReadAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default)
    {
        Task<CalendarSchedule> pending;
        bool sameRange;
        lock (_gate)
        {
            // A hung Outlook prompt must not spawn another COM thread on every tick.
            if (_pending is null || _pending.IsCompleted)
            {
                var completion = new TaskCompletionSource<CalendarSchedule>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending = completion.Task;
                _pendingFrom = from;
                _pendingUntil = until;
                var thread = new Thread(() =>
                {
                    try { completion.TrySetResult(Read(from, until)); }
                    catch (Exception ex) { completion.TrySetException(new InvalidOperationException(
                        "Could not read classic Outlook. Open classic Outlook with a configured profile, or choose a local .ics file.", ex)); }
                }) { IsBackground = true, Name = "Biscotti calendar" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
            pending = _pending;
            sameRange = _pendingFrom == from && _pendingUntil == until;
        }
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(20), ct);
        return sameRange ? result : await ReadAsync(from, until, ct);
    }

    private static CalendarSchedule Read(DateTimeOffset from, DateTimeOffset until)
    {
        dynamic? app = null, session = null, stores = null;
        var calendars = new List<CalendarInfo>();
        var events = new List<CalendarMeeting>();
        try
        {
            var type = Type.GetTypeFromProgID("Outlook.Application")
                ?? throw new InvalidOperationException("Classic Outlook is not installed. New Outlook does not provide local calendar access.");
            app = Activator.CreateInstance(type);
            session = app!.GetNamespace("MAPI");
            stores = session.Stores;
            for (var i = 1; i <= (int)stores.Count && i <= 32; i++)
            {
                dynamic? store = null, folder = null;
                try
                {
                    store = stores.Item(i);
                    try { folder = store.GetDefaultFolder(9); }
                    catch (COMException) { continue; }
                    ReadFolder(folder, (string)store.DisplayName, from, until, calendars, events, 0);
                }
                finally { Release(folder); Release(store); }
            }
            if (calendars.Count == 0) throw new InvalidOperationException("No calendar folders were found in classic Outlook.");
            return new(calendars, events.DistinctBy(e => e.EventId).OrderBy(e => e.StartsAt).ToArray());
        }
        finally { Release(stores); Release(session); Release(app); } // Never call Outlook.Quit.
    }

    private static void ReadFolder(dynamic folder, string source, DateTimeOffset from, DateTimeOffset until,
        List<CalendarInfo> calendars, List<CalendarMeeting> events, int depth)
    {
        if (depth > 8 || calendars.Count >= 128) return;
        dynamic? items = null, restricted = null, folders = null;
        try
        {
            var id = $"outlook:{(string)folder.StoreID}:{(string)folder.EntryID}";
            var title = IcsCalendarReader.Clean((string)folder.Name, 160) ?? "Outlook calendar";
            calendars.Add(new(id, title, source));
            items = folder.Items;
            items.Sort("[Start]");
            items.IncludeRecurrences = true;
            var begins = from.LocalDateTime.ToString("g", CultureInfo.CurrentCulture).Replace("'", "''");
            var ends = until.LocalDateTime.ToString("g", CultureInfo.CurrentCulture).Replace("'", "''");
            restricted = items.Restrict($"[Start] < '{ends}' AND [End] > '{begins}'");
            for (var i = 1; i <= 2000; i++)
            {
                dynamic? item = null, recipients = null;
                try
                {
                    // IncludeRecurrences collections may have an undefined Count.
                    item = i == 1 ? restricted.GetFirst() : restricted.GetNext();
                    if (item is null) break;
                    var start = new DateTimeOffset(DateTime.SpecifyKind((DateTime)item.StartUTC, DateTimeKind.Utc));
                    var end = new DateTimeOffset(DateTime.SpecifyKind((DateTime)item.EndUTC, DateTimeKind.Utc));
                    if (start >= until || end <= from || (int)item.MeetingStatus is 5 or 7) continue;
                    recipients = item.Recipients;
                    var count = (int)recipients.Count;
                    var attendees = new List<string>();
                    for (var j = 1; j <= count && j <= 50; j++)
                    {
                        dynamic? recipient = null;
                        try
                        {
                            recipient = recipients.Item(j);
                            var name = IcsCalendarReader.Clean((string)recipient.Name, 100);
                            if (name is not null) attendees.Add(name);
                        }
                        finally { Release(recipient); }
                    }
                    events.Add(new($"{id}:{(string)item.EntryID}:{start.UtcTicks}",
                        IcsCalendarReader.Clean((string)item.Subject, 160) ?? "Untitled meeting", start, end,
                        attendees.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        IcsCalendarReader.Clean((string)item.Body, 2000), IcsCalendarReader.Clean((string)item.Location, 300),
                        id, title, IsAllDay: (bool)item.AllDayEvent, TotalAttendees: count));
                }
                finally { Release(recipients); Release(item); }
            }
            folders = folder.Folders;
            for (var i = 1; i <= (int)folders.Count && i <= 128; i++)
            {
                dynamic? child = null;
                try
                {
                    child = folders.Item(i);
                    if ((int)child.DefaultItemType == 1) ReadFolder(child, source, from, until, calendars, events, depth + 1);
                }
                finally { Release(child); }
            }
        }
        finally { Release(folders); Release(restricted); Release(items); }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
