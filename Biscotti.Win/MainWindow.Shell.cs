using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Forms = System.Windows.Forms;

namespace Biscotti.Win;

public partial class MainWindow
{
    private Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _biscottiTrayIcon;
    private Forms.ToolStripMenuItem? _recordMenuItem;
    private Forms.ToolStripMenuItem? _openLastMenuItem;
    private Forms.ToolStripMenuItem? _calendarMenuItem;
    private Forms.ToolStripMenuItem? _recentMenuItem;
    private Forms.ToolStripMenuItem? _exitMenuItem;
    private Forms.ToolStripMenuItem? _keepRecordingMenuItem;
    private GlobalHotkeyRegistration? _hotkeyRegistration;
    private string? _hotkeyRegistrationError;
    private readonly System.Windows.Threading.DispatcherTimer _shellTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private readonly HashSet<string> _notifiedCalendarEventIds = new(StringComparer.Ordinal);
    private string? _trayCalendarSignature;
    private bool _shellInitialized;

    public void InitializeShell()
    {
        if (_shellInitialized) return;
        _shellInitialized = true;

        var menu = new Forms.ContextMenuStrip();
        _recordMenuItem = new Forms.ToolStripMenuItem("Start recording", null, (_, _) => ToggleRecordingFromShell());
        _openLastMenuItem = new Forms.ToolStripMenuItem("Open last meeting", null, (_, _) => OpenLastMeetingFromShell());
        _calendarMenuItem = new Forms.ToolStripMenuItem("Upcoming meetings");
        _recentMenuItem = new Forms.ToolStripMenuItem("Recent meetings");
        menu.Items.Add(_recordMenuItem);
        _keepRecordingMenuItem = new Forms.ToolStripMenuItem("Keep recording", null, (_, _) => KeepRecording_Click(this, new RoutedEventArgs())) { Visible = false };
        menu.Items.Add(_keepRecordingMenuItem);
        menu.Items.Add(_openLastMenuItem);
        menu.Items.Add(_calendarMenuItem);
        menu.Items.Add(_recentMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings", null, (_, _) => OpenSettingsFromShell()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        _exitMenuItem = new Forms.ToolStripMenuItem("Exit", null, (_, _) => ExitFromShell());
        menu.Items.Add(_exitMenuItem);

        _biscottiTrayIcon = LoadBiscottiTrayIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _biscottiTrayIcon,
            Text = "Biscotti Win — idle",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromShell();

        _meetings.StateChanged += OnShellMeetingStateChanged;
        SourceInitialized += (_, _) => InitializeGlobalHotkey();
        _shellTimer.Tick += (_, _) => UpdateTrayState(_meetings.State);
        _shellTimer.Start();
        UpdateTrayState(_meetings.State);
    }

    public void ShowFromShell()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void SetShellStatus(string message) => StatusText.Text = message;

    private void InitializeGlobalHotkey()
    {
        if (_designPreview) return;
        try
        {
            _hotkeyRegistration = new GlobalHotkeyRegistration(this);
            _hotkeyRegistration.Pressed += (_, _) => ToggleRecordingFromShell();
            if (!_hotkeyRegistration.TrySet(_settings.RecordingHotkey, out var error))
            {
                _hotkeyRegistrationError = error;
                StatusText.Text = $"{error} Open Settings to choose an available shortcut.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Global shortcut unavailable: {ex.Message}";
        }
    }

    private void OnShellMeetingStateChanged(RecordingState state, string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateTrayState(state);
            if (state == RecordingState.Idle && message.StartsWith("Done —", StringComparison.Ordinal))
                ShowSummaryNotification();
        });
    }

    private void UpdateTrayState(RecordingState state)
    {
        if (_trayIcon is null) return;
        var isRecording = state == RecordingState.Recording;
        var isBusy = state != RecordingState.Idle;
        var stateName = isRecording ? "recording" : isBusy ? "processing" : "idle";
        _trayIcon.Text = $"Biscotti Win — {stateName}";
        if (_recordMenuItem is not null)
        {
            _recordMenuItem.Text = isRecording ? "Stop recording" : "Start recording";
            _recordMenuItem.Enabled = isRecording || state == RecordingState.Idle && RecordButton.IsEnabled;
        }
        if (_openLastMenuItem is not null)
            _openLastMenuItem.Enabled = state == RecordingState.Idle && MeetingList.Items.Count > 0;
        if (_exitMenuItem is not null) _exitMenuItem.Enabled = state == RecordingState.Idle;
        UpdateCalendarTrayMenus(state);
    }

    private void UpdateCalendarTrayMenus(RecordingState state)
    {
        if (_calendarMenuItem is null || _recentMenuItem is null) return;
        var enabled = _settings.CalendarTrayNavigation && CalendarConnected;
        var recent = _historyDocuments.Select(document => document.Meeting).OrderByDescending(meeting => meeting.StartedUtc)
            .Take(5).ToArray();
        var signature = $"{enabled}:{state}:" + string.Join("|", _upcomingMeetings.Select(meeting => $"{meeting.EventId}:{meeting.Title}:{meeting.StartsAt.UtcTicks}")) + ":" +
            string.Join("|", recent.Select(meeting => $"{meeting.Id}:{meeting.Title}:{meeting.StartedUtc.Ticks}"));
        if (signature == _trayCalendarSignature) return;
        _trayCalendarSignature = signature;
        _calendarMenuItem.DropDownItems.Clear();
        _calendarMenuItem.Enabled = enabled && state == RecordingState.Idle;
        if (!enabled || _upcomingMeetings.Count == 0)
            _calendarMenuItem.DropDownItems.Add(new Forms.ToolStripMenuItem(enabled ? "No upcoming meetings" : "Enable in Settings") { Enabled = false });
        else
            foreach (var meeting in _upcomingMeetings.Take(5))
            {
                var eventId = meeting.EventId;
                var item = new Forms.ToolStripMenuItem($"Record: {meeting.Title} · {meeting.StartsAt.ToLocalTime():h:mm tt}");
                item.Click += async (_, _) => await StartCalendarMeetingFromShellAsync(eventId);
                _calendarMenuItem.DropDownItems.Add(item);
            }

        _recentMenuItem.DropDownItems.Clear();
        _recentMenuItem.Enabled = _settings.CalendarTrayNavigation && state == RecordingState.Idle && recent.Length > 0;
        if (!_settings.CalendarTrayNavigation || recent.Length == 0)
            _recentMenuItem.DropDownItems.Add(new Forms.ToolStripMenuItem("No recent meetings") { Enabled = false });
        else
            foreach (var meeting in recent)
            {
                var id = meeting.Id;
                var item = new Forms.ToolStripMenuItem($"{meeting.Title} · {meeting.StartedUtc.ToLocalTime():MMM d}");
                item.Click += (_, _) => OpenMeetingFromShell(id);
                _recentMenuItem.DropDownItems.Add(item);
            }
    }

    private async Task StartCalendarMeetingFromShellAsync(string eventId)
    {
        ShowFromShell();
        var requested = _upcomingMeetings.FirstOrDefault(meeting => meeting.EventId == eventId);
        if (requested is null) { await RefreshCalendarMeetingsAsync(); return; }
        await StartRecordingAsync(requested);
    }

    private void ToggleRecordingFromShell()
    {
        if (_meetings.State == RecordingState.Idle)
        {
            if (!RecordButton.IsEnabled)
            {
                ShowFromShell();
                StatusText.Text = "Download the Whisper model before starting a recording.";
                return;
            }
            RecordButton_Click(RecordButton, new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent, RecordButton));
        }
        else if (_meetings.State == RecordingState.Recording)
        {
            RecordButton_Click(RecordButton, new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent, RecordButton));
        }
    }

    private void OpenLastMeetingFromShell()
    {
        if (_designPreview) { OpenSelectedMeeting(); return; }
        ShowFromShell();
        try
        {
            var meeting = MeetingStore.LoadAll().FirstOrDefault();
            if (meeting is null)
            {
                StatusText.Text = "No meetings have been saved yet.";
                return;
            }
            new MeetingWindow(meeting, calendarEventsNear: CalendarEventsNearAsync) { Owner = this }.ShowDialog();
            RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open last meeting", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenMeetingFromShell(Guid id)
    {
        ShowFromShell();
        var meeting = _historyDocuments.Select(document => document.Meeting).FirstOrDefault(item => item.Id == id);
        if (meeting is null) { StatusText.Text = "That recent meeting is no longer available."; return; }
        new MeetingWindow(meeting, calendarEventsNear: CalendarEventsNearAsync) { Owner = this }.ShowDialog();
        RefreshList();
    }

    private void OpenSettingsFromShell()
    {
        ShowFromShell();
        if (_meetings.State != RecordingState.Idle || ModelProgress.Visibility == Visibility.Visible)
        {
            StatusText.Text = "Finish the current meeting or download before changing settings.";
            return;
        }
        var dialog = new SettingsWindow(_settings, SaveSettingsFromDialog, _hotkeyRegistrationError) { Owner = this };
        dialog.ShowDialog();
        UpdateModelButtons();
        _ = RefreshSummaryReadinessAsync();
    }

    private string? SaveSettingsFromDialog(Biscotti.Core.AppSettings candidate)
    {
        if (_designPreview) return null;
        if (_meetings.State != RecordingState.Idle) return "Finish the current meeting before saving settings.";
        var shortcut = candidate.RecordingHotkey ?? "Ctrl+Alt+R";
        var shortcutChanged = !string.Equals(shortcut, _settings.RecordingHotkey ?? "Ctrl+Alt+R", StringComparison.Ordinal);
        if (shortcutChanged && _hotkeyRegistration is null)
            return "The shortcut service is not ready yet. Try again in a moment.";
        var previousRegistered = _hotkeyRegistration?.Current;
        if (_hotkeyRegistration is not null && !_hotkeyRegistration.TrySet(shortcut, out var error))
        {
            _hotkeyRegistrationError = error;
            return error;
        }

        try
        {
            candidate.Save();
            var runtimeChanged = candidate.WhisperRuntime != _settings.WhisperRuntime;
            _settings.RecordingHotkey = shortcut;
            _settings.LocalVocabulary = candidate.LocalVocabulary;
            _settings.WhisperModelId = candidate.WhisperModelId;
            _settings.WhisperRuntime = candidate.WhisperRuntime;
            _settings.OllamaModel = candidate.OllamaModel;
            _settings.SummaryLength = candidate.SummaryLength;
            _settings.SummaryGuidance = candidate.SummaryGuidance;
            _meetings.SetAutoStopEnabled(candidate.AutoStopEnabled);
            AutoStopBox.IsChecked = candidate.AutoStopEnabled;
            _settings.CalendarStartNotifications = candidate.CalendarStartNotifications;
            _settings.CalendarTrayNavigation = candidate.CalendarTrayNavigation;
            _settings.CalendarNotificationMode = candidate.CalendarNotificationMode;
            _hotkeyRegistrationError = null;
            StatusText.Text = runtimeChanged ? "Settings saved locally. Restart Biscotti to apply the runtime change." : "Settings saved locally.";
            return null;
        }
        catch (Exception ex)
        {
            if (_hotkeyRegistration is not null)
            {
                if (previousRegistered is null) _hotkeyRegistration.Clear();
                else _hotkeyRegistration.TrySet(previousRegistered, out _);
            }
            _hotkeyRegistrationError = $"Could not save settings. {ex.Message}";
            return _hotkeyRegistrationError;
        }
    }

    private void ExitFromShell()
    {
        if (_meetings.State != RecordingState.Idle) return;
        App.RequestExit();
    }

    private static void ShowSummaryNotification()
    {
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText("Meeting summary ready")
                .AddText("Open Biscotti Win to review your meeting.")
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch { /* Windows may disable app notifications or Focus Assist may suppress delivery. */ }
    }

    private static System.Drawing.Icon LoadBiscottiTrayIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Biscotti.Win;component/Assets/biscotti.ico"))
            ?? throw new InvalidOperationException("The Biscotti tray icon resource is missing.");
        using var stream = resource.Stream;
        using var icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)icon.Clone();
    }

    private void MaybeShowCalendarNotification()
    {
        if (!_settings.CalendarStartNotifications || !CalendarConnected) return;
        if (_meetings.State != RecordingState.Idle || _settings.CalendarNotificationMode == "never") return;
        _notifiedCalendarEventIds.IntersectWith(_upcomingMeetings.Select(e => e.EventId));
        var now = DateTimeOffset.Now;
        foreach (var meeting in _upcomingMeetings.Where(meeting => meeting.StartsAt >= now && meeting.StartsAt <= now.AddMinutes(5)
            && (_settings.CalendarNotificationMode != "video" || meeting.ConferenceUrl is not null)))
        {
            if (!_notifiedCalendarEventIds.Add(meeting.EventId)) continue;
            try
            {
                var notification = new AppNotificationBuilder()
                    .AddText("Calendar event starting")
                    .AddText("Open Biscotti Win to review your current local calendar.")
                    .BuildNotification();
                AppNotificationManager.Default.Show(notification);
            }
            catch { /* Windows notification settings or Focus Assist may suppress delivery. */ }
        }
    }

    private void DisposeShell()
    {
        UpdateAutoStopNotice(null);
        _shellTimer.Stop();
        _hotkeyRegistration?.Dispose();
        _hotkeyRegistration = null;
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _biscottiTrayIcon?.Dispose();
        _biscottiTrayIcon = null;
    }
}
