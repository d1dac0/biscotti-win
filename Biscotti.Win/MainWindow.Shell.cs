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
    private Forms.ToolStripMenuItem? _recordMenuItem;
    private Forms.ToolStripMenuItem? _openLastMenuItem;
    private Forms.ToolStripMenuItem? _exitMenuItem;
    private GlobalHotkeyRegistration? _hotkeyRegistration;
    private string? _hotkeyRegistrationError;
    private readonly System.Windows.Threading.DispatcherTimer _shellTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private bool _shellInitialized;

    public void InitializeShell()
    {
        if (_shellInitialized) return;
        _shellInitialized = true;

        var menu = new Forms.ContextMenuStrip();
        _recordMenuItem = new Forms.ToolStripMenuItem("Start recording", null, (_, _) => ToggleRecordingFromShell());
        _openLastMenuItem = new Forms.ToolStripMenuItem("Open last meeting", null, (_, _) => OpenLastMeetingFromShell());
        menu.Items.Add(_recordMenuItem);
        menu.Items.Add(_openLastMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings", null, (_, _) => OpenSettingsFromShell()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        _exitMenuItem = new Forms.ToolStripMenuItem("Exit", null, (_, _) => ExitFromShell());
        menu.Items.Add(_exitMenuItem);

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
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
        _trayIcon.Icon = isRecording
            ? System.Drawing.SystemIcons.Information
            : isBusy ? System.Drawing.SystemIcons.Warning : System.Drawing.SystemIcons.Application;
        if (_recordMenuItem is not null)
        {
            _recordMenuItem.Text = isRecording ? "Stop recording" : "Start recording";
            _recordMenuItem.Enabled = isRecording || state == RecordingState.Idle && RecordButton.IsEnabled;
        }
        if (_openLastMenuItem is not null)
            _openLastMenuItem.Enabled = state == RecordingState.Idle && MeetingList.Items.Count > 0;
        if (_exitMenuItem is not null) _exitMenuItem.Enabled = state == RecordingState.Idle;
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
        ShowFromShell();
        try
        {
            var meeting = MeetingStore.LoadAll().FirstOrDefault();
            if (meeting is null)
            {
                StatusText.Text = "No meetings have been saved yet.";
                return;
            }
            new MeetingWindow(meeting) { Owner = this }.ShowDialog();
            RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open last meeting", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSettingsFromShell()
    {
        ShowFromShell();
        var dialog = new SettingsWindow(
            _settings.RecordingHotkey ?? "Ctrl+Alt+R", _settings.LocalVocabulary ?? "",
            SaveSettingsFromDialog, _hotkeyRegistrationError) { Owner = this };
        dialog.ShowDialog();
    }

    private string? SaveSettingsFromDialog(string shortcut, string vocabulary)
    {
        var shortcutChanged = !string.Equals(shortcut, _settings.RecordingHotkey ?? "Ctrl+Alt+R", StringComparison.Ordinal);
        if (shortcutChanged && _hotkeyRegistration is null)
            return "The shortcut service is not ready yet. Try again in a moment.";
        if (_hotkeyRegistration is not null && !_hotkeyRegistration.TrySet(shortcut, out var error))
        {
            _hotkeyRegistrationError = error;
            return error;
        }

        var previousSetting = _settings.RecordingHotkey;
        var previousVocabulary = _settings.LocalVocabulary;
        var previousRegistered = _hotkeyRegistration?.Current;
        _settings.RecordingHotkey = shortcut;
        _settings.LocalVocabulary = vocabulary;
        try
        {
            _settings.Save();
            _hotkeyRegistrationError = null;
            StatusText.Text = "Settings saved locally.";
            return null;
        }
        catch (Exception ex)
        {
            _settings.RecordingHotkey = previousSetting;
            _settings.LocalVocabulary = previousVocabulary;
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

    private void DisposeShell()
    {
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
    }
}
