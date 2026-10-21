namespace Biscotti.Core.Calendar;

/// <summary>Serializes source queries and discards results after a connection/selection change.</summary>
public sealed class CalendarService : IDisposable
{
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly object _stateGate = new();
    private ICalendarSource? _source;
    private string[]? _enabled;
    private int _revision;
    private CancellationTokenSource _configuration = new();
    public CalendarSchedule Schedule { get; private set; } = new([], []);

    public void Configure(ICalendarSource? source, IEnumerable<string>? enabled)
    {
        lock (_stateGate)
        {
            _configuration.Cancel();
            _configuration.Dispose();
            _configuration = new();
            _source = source;
            _enabled = enabled?.ToArray();
            _revision++;
            Schedule = new([], []);
        }
    }

    public async Task<bool> RefreshAsync(DateTimeOffset now)
    {
        await _readGate.WaitAsync();
        try
        {
            ICalendarSource? source; int revision; CancellationToken ct;
            lock (_stateGate) { source = _source; revision = _revision; ct = _configuration.Token; }
            CalendarSchedule schedule;
            try { schedule = source is null ? new([], []) : await source.ReadAsync(now, now + CalendarQuery.UpcomingWindow, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return false; }
            catch
            {
                lock (_stateGate) { if (revision == _revision) Schedule = new([], []); }
                throw;
            }
            lock (_stateGate)
            {
                if (revision != _revision) return false;
                Schedule = schedule;
                return true;
            }
        }
        finally { _readGate.Release(); }
    }

    public IReadOnlyList<CalendarMeeting> Upcoming(DateTimeOffset now)
    {
        lock (_stateGate) return CalendarQuery.Upcoming(Schedule, now, _enabled);
    }

    public async Task<IReadOnlyList<CalendarMeeting>> EventsNearAsync(DateTimeOffset date)
    {
        await _readGate.WaitAsync();
        try
        {
            ICalendarSource? source; int revision; string[]? enabled; CancellationToken ct;
            lock (_stateGate) { source = _source; revision = _revision; enabled = _enabled; ct = _configuration.Token; }
            if (source is null) return [];
            CalendarSchedule schedule;
            try { schedule = await source.ReadAsync(date.AddMinutes(-90), date.AddMinutes(90).AddTicks(1), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return []; }
            lock (_stateGate) return revision == _revision ? CalendarQuery.Near(schedule, date, enabled) : [];
        }
        finally { _readGate.Release(); }
    }

    public void Dispose()
    {
        lock (_stateGate) { _revision++; _configuration.Cancel(); _source = null; Schedule = new([], []); }
    }
}
