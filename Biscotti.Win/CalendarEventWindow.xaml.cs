using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Biscotti.Core.Calendar;

namespace Biscotti.Win;

public partial class CalendarEventWindow : Window
{
    private readonly Func<CalendarMeeting, Task<CalendarMeeting?>> _resolve;
    private readonly Func<CalendarMeeting, Task> _action;
    private readonly bool _designPreview;
    private bool _busy;

    public CalendarEventWindow(IReadOnlyList<CalendarMeeting> events,
        Func<CalendarMeeting, Task<CalendarMeeting?>> resolve, Func<CalendarMeeting, Task> action,
        bool linkMode = false, bool designPreview = false)
    {
        InitializeComponent();
        _resolve = resolve;
        _action = action;
        _designPreview = designPreview;
        Heading.Text = linkMode ? "Link a calendar event" : "Upcoming meeting";
        EventActionButton.Content = linkMode ? "Link event" : "Record meeting";
        EventPicker.ItemsSource = events;
        EventPicker.SelectedIndex = events.Count > 0 ? 0 : -1;
        EventPicker.Visibility = linkMode ? Visibility.Visible : Visibility.Collapsed;
        if (events.Count == 0) EventStatus.Text = "No calendar events near this recording. Connect calendars on the main screen or choose another calendar.";
        RefreshEvent();
    }

    private void Event_Changed(object sender, SelectionChangedEventArgs e) => RefreshEvent();

    private void RefreshEvent()
    {
        if (JoinButton is null) return;
        var ev = EventPicker.SelectedItem as CalendarMeeting;
        EventTitle.Text = ev?.Title ?? "No events found";
        EventWhen.Text = ev?.WhenDisplay ?? "";
        EventCalendar.Text = ev is null ? "" : $"{ev.CalendarTitle} · {ev.Platform ?? "Calendar meeting"}";
        EventAttendees.Text = ev?.AttendeesDisplay ?? "";
        EventLocation.Text = string.IsNullOrWhiteSpace(ev?.Location) ? "No location specified" : ev.Location;
        EventDescription.Text = string.IsNullOrWhiteSpace(ev?.Description) ? "No description" : ev.Description;
        JoinButton.IsEnabled = ev?.ConferenceUrl is not null && ev.EndsAt > DateTimeOffset.Now && !_busy;
        EventActionButton.IsEnabled = ev is not null && !_busy;
    }

    private async void Join_Click(object sender, RoutedEventArgs e) => await ActAsync(join: true);
    private async void Action_Click(object sender, RoutedEventArgs e) => await ActAsync(join: false);

    private async Task ActAsync(bool join)
    {
        if (_busy || EventPicker.SelectedItem is not CalendarMeeting ev) return;
        if (_designPreview)
        {
            EventStatus.Text = join ? "Preview only · no meeting link opened." : "Preview only · no recording started or event saved.";
            return;
        }
        _busy = true;
        EventPicker.IsEnabled = false;
        RefreshEvent();
        try
        {
            var fresh = await _resolve(ev);
            if (fresh is null)
            {
                EventStatus.Text = "This event changed or is no longer available. Close this preview and refresh the calendar.";
                return;
            }
            if (join)
            {
                var uri = fresh.ConferenceUrl;
                if (uri is null) { EventStatus.Text = "This event no longer has a supported meeting link."; return; }
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            else { await _action(fresh); if (IsVisible) Close(); }
        }
        catch { EventStatus.Text = "The calendar action could not finish. Refresh the calendar and try again."; }
        finally { _busy = false; EventPicker.IsEnabled = true; RefreshEvent(); }
    }
}
