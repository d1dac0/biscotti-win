using System.Windows;
using Biscotti.Core.Calendar;

namespace Biscotti.Win;

public partial class MeetingWindow
{
    private readonly Func<DateTimeOffset, Task<IReadOnlyList<CalendarMeeting>>>? _calendarEventsNear;
    private readonly Action<CalendarMeeting?>? _setCalendarEvent;
    private bool _linkingCalendar;

    private void RefreshCalendarContext()
    {
        var ev = _meeting.CalendarEvent;
        CalendarContextTitle.Text = ev?.Title ?? (_meeting.CalendarAttendees.Count > 0
            ? "Invitee context from an earlier calendar recording" : "No calendar event linked");
        CalendarContextText.Text = ev is null ? "" : $"{ev.WhenDisplay} · {ev.CalendarTitle}\nInvited: {ev.AttendeesDisplay}\n{ev.Location}\n{ev.Description}";
        CalendarContextDetails.Visibility = ev is null ? Visibility.Collapsed : Visibility.Visible;
        LinkCalendarButton.Content = ev is null ? "Link event…" : "Change event…";
        LinkCalendarButton.IsEnabled = _calendarEventsNear is not null && !_isProcessing && !_processingActionRunning && !_linkingCalendar;
        RemoveCalendarLinkButton.Visibility = ev is null && _meeting.CalendarAttendees.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RemoveCalendarLinkButton.IsEnabled = !_isProcessing && !_processingActionRunning && !_linkingCalendar;
    }

    private async void LinkCalendar_Click(object sender, RoutedEventArgs e)
    {
        if (_calendarEventsNear is null || _linkingCalendar || _isProcessing || _processingActionRunning) return;
        if (!_isLiveRecording && HasUnsavedEdits && !TrySaveEdits()) return;
        _linkingCalendar = true;
        RefreshCalendarContext();
        CalendarLinkStatus.Text = "Looking for calendar events near this recording…";
        var date = new DateTimeOffset(DateTime.SpecifyKind(_meeting.StartedUtc, DateTimeKind.Utc));
        try
        {
            var events = await _calendarEventsNear(date);
            if (!IsLoaded) return;
            new CalendarEventWindow(events, async requested =>
            {
                var fresh = await _calendarEventsNear(date);
                return fresh.FirstOrDefault(e => e.EventId == requested.EventId);
            }, ev =>
            {
                SetCalendarAssociation(ev);
                return Task.CompletedTask;
            }, linkMode: true, designPreview: _designPreview) { Owner = this }.ShowDialog();
            CalendarLinkStatus.Text = _designPreview ? "Preview only · no calendar data opened or saved." : "Calendar context is saved locally. Invitees are not confirmed participants.";
        }
        catch { CalendarLinkStatus.Text = "Could not read the calendar. Reconnect calendars on the main screen, then try again."; }
        finally { _linkingCalendar = false; RefreshCalendarContext(); }
    }

    private void RemoveCalendarLink_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing || _processingActionRunning || _linkingCalendar) return;
        if (!_isLiveRecording && HasUnsavedEdits && !TrySaveEdits()) return;
        try
        {
            SetCalendarAssociation(null);
            CalendarLinkStatus.Text = "Calendar link removed. Existing title, notes and speaker names are unchanged.";
        }
        catch { CalendarLinkStatus.Text = "Could not save the change. The previous calendar link is still attached."; }
    }

    internal void SetCalendarAssociation(CalendarMeeting? ev)
    {
        if (_isProcessing || _processingActionRunning) throw new InvalidOperationException("Meeting processing is in progress.");
        if (_isLiveRecording)
        {
            if (_setCalendarEvent is null) throw new InvalidOperationException("Live calendar editing is unavailable.");
            _setCalendarEvent(ev);
        }
        else CalendarAssociation.Set(_meeting, ev, _saveMeeting);
        CalendarLinkStatus.Text = "Calendar context saved locally. Invitees are not confirmed participants.";
        RefreshCalendarContext();
    }
}
