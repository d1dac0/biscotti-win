using System.Windows;
using System.Windows.Controls;
using Biscotti.Core.Calendar;
using Biscotti.Core.Models;

namespace Biscotti.Win;

/// <summary>Opt-in, in-memory fixture for repeatable visual review. It bypasses
/// the normal startup path, profile settings, storage, model downloads and capture.</summary>
internal static class DesignPreview
{
    public static bool TryShow(string[] args)
    {
        var argument = args.FirstOrDefault(a => a == "--design-preview" || a.StartsWith("--design-preview=", StringComparison.Ordinal));
        if (argument is null) return false;
        var mode = argument.Contains('=') ? argument.Split('=', 2)[1] : "history";
        if (!new[] { "setup", "empty", "history", "recording", "processing", "failure", "review", "transfer", "tray" }.Contains(mode))
        {
            MessageBox.Show("Preview states: setup, empty, history, recording, processing, failure, review, transfer, tray.", "Design preview");
            System.Windows.Application.Current.Shutdown();
            return true;
        }
        var window = new MainWindow(designPreview: true);
        window.ConfigureDesignPreview(mode);
        var app = System.Windows.Application.Current;
        app.MainWindow = window;
        app.ShutdownMode = ShutdownMode.OnLastWindowClose;
        window.Closed += async (_, _) => await window.DisposeForShutdownAsync();
        window.Show();
        if (mode == "recording") window.TogglePreviewRecording();
        if (mode == "review") new MeetingWindow(CreateMeeting(), designPreview: true) { Owner = window }.Show();
        if (mode == "transfer")
            new ImportPreviewWindow(new Biscotti.Core.Storage.ImportPreview(new[] { CreateMeeting() }, 2,
                new[] { "Row 4: invalid fields, dates or timeline. Check the CSV format." }))
                { Owner = window }.ShowDialog();
        if (mode == "tray") window.ShowPreviewTray();
        return true;
    }

    internal static Meeting CreateMeeting() => new()
    {
        Id = Guid.Parse("00000079-0000-0000-0000-000000000001"),
        Title = "Project Atlas planning",
        StartedUtc = new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc),
        EndedUtc = new DateTime(2026, 10, 20, 15, 18, 0, DateTimeKind.Utc),
        Summary = "## Decisions\n\nThe team will test the Atlas prototype on Thursday.\n\n## Next steps\n\nAlex will share the checklist. Jordan will review the results.",
        Notes = "## My notes\n\n- Keep the first test small.\n- Review accessibility before release.",
        ActionItems = new() { "Alex: share the test checklist by Wednesday.", "Jordan: review the results on Friday." },
        Transcript = new()
        {
            new() { Speaker = "Alex", StartSeconds = 12, EndSeconds = 20, Text = "Let's keep the Atlas prototype test focused on the main workflow." },
            new() { Speaker = "Jordan", StartSeconds = 24, EndSeconds = 32, Text = "Agreed. Thursday works for the test. I'll review the results on Friday." },
            new() { Speaker = "Alex", StartSeconds = 38, EndSeconds = 46, Text = "I'll share the checklist by Wednesday, including the accessibility checks." },
        },
        TimestampedNotes = new() { new() { StartSeconds = 38, Text = "Checklist due Wednesday. Include keyboard navigation." } },
    };
}

public partial class MainWindow
{
    internal void ConfigureDesignPreview(string mode)
    {
        if (!_designPreview) throw new InvalidOperationException("Preview requires isolated startup.");
        Title = "Biscotti Win — synthetic design preview";
        PreviewBanner.Visibility = Visibility.Visible;
        var meeting = DesignPreview.CreateMeeting();
        var empty = mode is "setup" or "empty";
        SetHistory(empty ? new List<Meeting>() : new List<Meeting> { meeting }, empty ? null : meeting.Id);
        AudioAppPicker.ItemsSource = new[] { new AudioAppChoice(null, null, "All speaker audio (synthetic preview)") };
        AudioAppPicker.SelectedIndex = 0;
        ModelButton.Visibility = mode == "setup" ? Visibility.Visible : Visibility.Collapsed;
        RecordButton.IsEnabled = mode != "setup" && mode != "processing";
        UpdateRecordingPresentation(RecordingState.Idle, mode != "setup");
        StatusText.Text = mode == "setup" ? "Download the model to enable recording. Preview does not download files."
            : "Ready for a private meeting. Preview does not record audio.";
        CalendarSourceText.Text = empty ? "" : "Synthetic calendar · no calendar file opened";
        CalendarEmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty)
            UpcomingMeetingsList.ItemsSource = new[]
            {
                new CalendarMeeting("synthetic-atlas", "Atlas design review",
                    new DateTimeOffset(2026, 10, 20, 16, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 10, 20, 16, 30, 0, TimeSpan.Zero), new[] { "Alex", "Jordan" }),
            };
        ChooseCalendarButton.IsEnabled = RefreshCalendarButton.IsEnabled = false;
        if (mode == "processing")
        {
            UpdateRecordingPresentation(RecordingState.Transcribing);
            StatusText.Text = "Finishing the live transcript…";
            AudioDevicesButton.IsEnabled = false;
            AudioAppPicker.IsEnabled = false;
            UpcomingMeetingsList.IsEnabled = false;
        }
        if (mode == "failure")
        {
            DeviceWarningText.Text = "The selected microphone is unavailable. Reconnect it, then choose Audio devices and try again.";
            DeviceWarningText.Visibility = Visibility.Visible;
            ShowHistoryFailure();
            MeetingList.ItemsSource = new List<Meeting>();
            StatusText.Text = "Recording did not start. Check Audio devices and try again.";
        }
    }

    internal void TogglePreviewRecording()
    {
        if (!_designPreview) throw new InvalidOperationException("Preview requires isolated startup.");
        _recording = !_recording;
        UpdateRecordingPresentation(_recording ? RecordingState.Recording : RecordingState.Idle, true);
        RecordButton.Content = _recording ? "■  Stop & save" : "●  Start recording";
        RecordingDurationText.Text = "Recording duration  00:02:18";
        RecordingDurationText.Visibility = _recording ? Visibility.Visible : Visibility.Collapsed;
        AudioAppPicker.IsEnabled = !_recording;
        AudioDevicesButton.IsEnabled = !_recording;
        UpcomingMeetingsList.IsEnabled = !_recording;
        StatusText.Text = _recording ? "Synthetic recording · microphone and system audio · nothing is captured."
            : "Synthetic recording finished. No data was saved.";
        if (_recording)
        {
            var meeting = DesignPreview.CreateMeeting();
            meeting.Summary = null;
            meeting.ActionItems.Clear();
            _liveMeetingWindow = new MeetingWindow(meeting, isLiveRecording: true,
                addTimestampedNote: (text, seconds) =>
                {
                    meeting.TimestampedNotes.Add(new() { Text = text, StartSeconds = seconds });
                    _liveMeetingWindow?.RefreshTranscript();
                }, recordingElapsedSeconds: () => 138, designPreview: true) { Owner = this };
            _liveMeetingWindow.Show();
        }
        else _liveMeetingWindow?.SetRecordingComplete();
        UpdateTrayState(_recording ? RecordingState.Recording : RecordingState.Idle);
    }

    internal void ShowPreviewTray()
    {
        InitializeShell();
        _shellTimer.Stop();
        UpdateTrayState(_recording ? RecordingState.Recording : RecordingState.Idle);
        StatusText.Text = "Synthetic tray preview · right-click the Biscotti tray icon for recording, review, settings and exit.";
    }

    private void PreviewTrayMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!_designPreview) return;
        ShowPreviewTray();
        var point = PointToScreen(new System.Windows.Point(40, 180));
        _trayIcon?.ContextMenuStrip?.Show(new System.Drawing.Point((int)point.X, (int)point.Y));
    }
}
