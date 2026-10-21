using System.Windows;
using System.Windows.Controls;
using Biscotti.Core.Calendar;

namespace Biscotti.Win;

public partial class MainWindow
{
    private readonly CalendarService _calendar = new();
    private readonly OutlookCalendarSource _outlookCalendar = new();
    private Task? _calendarRefresh;
    private bool _calendarStartPending;
    private bool _calendarConfigurationOpen;
    private readonly CalendarConnectionStore _calendarConnections = new();
    private List<CalendarConnection> _providerConnections = [];
    private bool _providerAccessError;
    private bool CalendarConnected => _settings.UseOutlookCalendar || !string.IsNullOrWhiteSpace(_settings.CalendarFilePath) || _providerConnections.Count > 0;

    private void DashboardGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (CalendarPanel is null || MainPrimaryPanel is null) return;
        var wide = e.NewSize.Width >= 860;
        MainPrimaryPanel.MaxWidth = Math.Max(0, e.NewSize.Width - (wide ? 294 : 0));
        DashboardGrid.ColumnDefinitions[0].Width = wide ? new GridLength(270) : new GridLength(1, GridUnitType.Star);
        DashboardGrid.ColumnDefinitions[1].Width = new GridLength(wide ? 24 : 0);
        DashboardGrid.ColumnDefinitions[2].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(MainPrimaryPanel, wide ? 2 : 0);
        Grid.SetRow(MainPrimaryPanel, wide ? 0 : 1);
    }

    private void ConfigureCalendarSource()
    {
        try
        {
            _providerConnections = _calendarConnections.Load();
            if (_providerConnections.Any(c => c.Provider is not ("google" or "microsoft" or "icloud")))
                throw new InvalidOperationException();
            _providerAccessError = false;
        }
        catch { _providerConnections = []; _providerAccessError = true; }
        var sources = _providerConnections.Select(c => (c.Label, CalendarProviders.Create(c))).ToList();
        if (_settings.UseOutlookCalendar) sources.Add(("Classic Outlook", _outlookCalendar));
        if (!string.IsNullOrWhiteSpace(_settings.CalendarFilePath)) sources.Add(("Local export", new IcsCalendarSource(_settings.CalendarFilePath)));
        _calendar.Configure(new CompositeCalendarSource(sources), _settings.EnabledCalendarIds);
    }

    private void UpdateCalendarControls()
    {
        var canEdit = _meetings.State == Core.Models.RecordingState.Idle && !_calendarStartPending;
        ChooseCalendarButton.IsEnabled = RefreshCalendarButton.IsEnabled = canEdit;
        RemoveCalendarButton.IsEnabled = canEdit && (CalendarConnected || _providerAccessError);
        UpcomingMeetingsList.IsEnabled = canEdit;
    }

    private Task RefreshCalendarMeetingsAsync(bool reportErrors = false)
    {
        if (_designPreview) return Task.CompletedTask;
        return _calendarRefresh is { IsCompleted: false } ? _calendarRefresh : _calendarRefresh = RefreshCalendarCoreAsync(reportErrors);
    }

    private async Task RefreshCalendarCoreAsync(bool reportErrors)
    {
        try
        {
            if (!await _calendar.RefreshAsync(DateTimeOffset.Now)) return;
            _upcomingMeetings = _calendar.Upcoming(DateTimeOffset.Now);
            UpcomingMeetingsList.ItemsSource = _upcomingMeetings;
            CalendarSourceText.Text = _providerAccessError ? "Saved provider access could not be unlocked. Open Calendars to reconnect."
                : _calendar.Schedule.Warnings is { Count: > 0 } warnings ? string.Join("\n", warnings)
                : !CalendarConnected ? "" : $"Connected calendars · refreshed {DateTime.Now:t}";
            CalendarEmptyText.Text = !CalendarConnected ? "Connect calendars to see your upcoming meetings."
                : _settings.EnabledCalendarIds is { Count: 0 } ? "No calendars selected. Open Calendars to choose which ones to show."
                : "No meetings in the next 7 days. Meetings need a conference link or at least two invitees.";
            CalendarEmptyText.Visibility = _upcomingMeetings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            MaybeShowCalendarNotification();
        }
        catch (Exception ex)
        {
            _upcomingMeetings = [];
            UpcomingMeetingsList.ItemsSource = _upcomingMeetings;
            CalendarSourceText.Text = _settings.UseOutlookCalendar ? "Classic Outlook unavailable" : "Calendar file unavailable";
            CalendarEmptyText.Text = ex is TimeoutException
                ? "Outlook is taking too long. Check Outlook for a prompt or use a local .ics file."
                : "Could not read the calendar. Open Calendars to reconnect or choose another file.";
            CalendarEmptyText.Visibility = Visibility.Visible;
            if (reportErrors) StatusText.Text = CalendarEmptyText.Text;
        }
        finally { UpdateCalendarControls(); }
    }

    private async void ChooseCalendarButton_Click(object sender, RoutedEventArgs e)
    {
        if (_designPreview) { new CalendarConnectionWindow(_settings, _outlookCalendar, true) { Owner = this }.ShowDialog(); return; }
        if (_meetings.State != Core.Models.RecordingState.Idle || _calendarStartPending || _calendarConfigurationOpen) return;
        _calendarConfigurationOpen = true;
        _calendarTimer.Stop();
        try
        {
            if (_calendarRefresh is not null) await _calendarRefresh;
            var dialog = new CalendarConnectionWindow(_settings, _outlookCalendar, connections: _providerConnections) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var oldPath = _settings.CalendarFilePath;
            var oldOutlook = _settings.UseOutlookCalendar;
            var oldSelection = _settings.EnabledCalendarIds;
            try
            {
                _settings.CalendarFilePath = dialog.FilePath;
                _settings.UseOutlookCalendar = dialog.UseOutlook;
                _settings.EnabledCalendarIds = dialog.SelectedIds;
                _settings.Save();
                _calendarConnections.Save(dialog.Connections);
            }
            catch
            {
                _settings.CalendarFilePath = oldPath;
                _settings.UseOutlookCalendar = oldOutlook;
                _settings.EnabledCalendarIds = oldSelection;
                try { _settings.Save(); } catch { }
                StatusText.Text = "Could not save the calendar connection. Your previous selection is unchanged.";
                return;
            }
            _notifiedCalendarEventIds.Clear();
            ConfigureCalendarSource();
            await RefreshCalendarMeetingsAsync(true);
        }
        finally { _calendarConfigurationOpen = false; _calendarTimer.Start(); }
    }

    private async void RemoveCalendarButton_Click(object sender, RoutedEventArgs e)
    {
        if (_designPreview || _meetings.State != Core.Models.RecordingState.Idle || _calendarStartPending) return;
        if (_calendarRefresh is not null) await _calendarRefresh;
        var path = _settings.CalendarFilePath;
        var outlook = _settings.UseOutlookCalendar;
        var selection = _settings.EnabledCalendarIds;
        try
        {
            _settings.CalendarFilePath = null;
            _settings.UseOutlookCalendar = false;
            _settings.EnabledCalendarIds = null;
            _settings.Save();
            _calendarConnections.Save([]);
            ConfigureCalendarSource();
            _notifiedCalendarEventIds.Clear();
            _trayCalendarSignature = null;
            await RefreshCalendarMeetingsAsync();
        }
        catch
        {
            _settings.CalendarFilePath = path;
            _settings.UseOutlookCalendar = outlook;
            _settings.EnabledCalendarIds = selection;
            try { _settings.Save(); } catch { }
            StatusText.Text = "Could not remove the calendar connection. Try again.";
        }
    }

    private async void RefreshCalendarButton_Click(object sender, RoutedEventArgs e) => await RefreshCalendarMeetingsAsync(true);

    private void PreviewCalendarEvent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CalendarMeeting ev }) return;
        new CalendarEventWindow([ev], ResolveUpcomingEventAsync, async fresh =>
        {
            await StartRecordingAsync(fresh);
            if (!_recording) throw new InvalidOperationException("Recording did not start.");
        }, designPreview: _designPreview) { Owner = this }.Show();
    }

    private async Task<CalendarMeeting?> ResolveUpcomingEventAsync(CalendarMeeting requested)
    {
        await RefreshCalendarMeetingsAsync(true);
        return _upcomingMeetings.FirstOrDefault(e => e.EventId == requested.EventId);
    }

    private Task<IReadOnlyList<CalendarMeeting>> CalendarEventsNearAsync(DateTimeOffset date) => _designPreview
        ? Task.FromResult<IReadOnlyList<CalendarMeeting>>((UpcomingMeetingsList.ItemsSource as IEnumerable<CalendarMeeting>)?.ToArray() ?? [])
        : _calendar.EventsNearAsync(date);
}
