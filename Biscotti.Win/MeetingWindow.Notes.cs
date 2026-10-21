using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Biscotti.Core.Models;

namespace Biscotti.Win;

public partial class MeetingWindow
{
    private readonly Action<Meeting> _saveMeeting;
    private List<TimestampedMeetingNote>? _noteDrafts;
    private string? _savedEditState;
    private bool _discardEditsOnClose;

    private void InitializeEditing()
    {
        TitleBox.Text = _meeting.Title;
        SummaryBox.Text = _meeting.Summary ?? "";
        NotesBox.Text = _meeting.Notes ?? "";
        CaptureSavedEdits();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save,
            (_, _) => TrySaveEdits(), (_, e) => e.CanExecute = !_isLiveRecording && !_isProcessing && !_processingActionRunning));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));
        Closing += ConfirmUnsavedEdits;
    }

    private string EditState() => JsonSerializer.Serialize(new
    {
        Title = TitleBox.Text, Summary = CanonicalText(SummaryBox.Text), Notes = CanonicalText(NotesBox.Text),
        TimestampedNotes = _noteDrafts?.Select(note => new { note.StartSeconds, Text = CanonicalText(note.Text) }),
    });

    // WPF normalizes line endings when an editor is first laid out. That is not
    // an edit; retain the original source bytes when its text has not changed.
    private static string CanonicalText(string? text) => (text ?? "").Replace("\r\n", "\n");
    private static string? PreserveSource(string edited, string? original) =>
        CanonicalText(edited) == CanonicalText(original) ? original : edited;

    internal bool HasUnsavedEdits => _savedEditState is not null && EditState() != _savedEditState;

    private void CaptureSavedEdits()
    {
        _savedEditState = EditState();
        SaveStatus.Text = _isLiveRecording ? "Timestamp notes save when added" : "Saved locally";
    }

    private void Edit_Changed(object sender, TextChangedEventArgs e)
    {
        if (_savedEditState is null || _isLiveRecording) return;
        SaveStatus.Text = HasUnsavedEdits ? "Unsaved changes · Save or Ctrl+S" : "Saved locally";
    }

    private void TimestampEdit_Changed(object sender, TextChangedEventArgs e)
    {
        // Ensure dirty state sees the source value even if the binding's handler
        // runs after TextChanged. Timestamp values themselves are never edited.
        ((TextBox)sender).GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Edit_Changed(sender, e);
    }

    private Meeting CreateEditedMeeting()
    {
        // Clone the complete current model so edits cannot leak into another
        // action's save or change the shared meeting after a failed write.
        var draft = JsonSerializer.Deserialize<Meeting>(JsonSerializer.Serialize(_meeting))!;
        draft.Title = TitleBox.Text;
        draft.Summary = PreserveSource(SummaryBox.Text, _meeting.Summary);
        draft.Notes = PreserveSource(NotesBox.Text, _meeting.Notes);
        draft.TimestampedNotes = (_noteDrafts ?? _meeting.TimestampedNotes).Select((note, index) =>
            new TimestampedMeetingNote
            {
                Text = PreserveSource(note.Text, _meeting.TimestampedNotes[index].Text) ?? "",
                StartSeconds = note.StartSeconds,
            }).ToList();
        return draft;
    }

    internal bool TrySaveEdits()
    {
        if (_isLiveRecording || _isProcessing || _processingActionRunning) return false;
        try
        {
            var draft = CreateEditedMeeting();
            _saveMeeting(draft);
            _meeting.Title = draft.Title;
            _meeting.Summary = draft.Summary;
            _meeting.Notes = draft.Notes;
            _meeting.TimestampedNotes = draft.TimestampedNotes;
            GetBindingExpression(TitleProperty)?.UpdateTarget();
            CaptureSavedEdits();
            if (_designPreview) SaveStatus.Text = "Saved in preview only · no files written";
            return true;
        }
        catch (Exception ex)
        {
            SaveStatus.Text = $"Not saved · edits kept here. Check storage access and retry Save. {ex.Message}";
            return false;
        }
    }

    private void ConfirmUnsavedEdits(object? sender, CancelEventArgs e)
    {
        if (_discardEditsOnClose || !HasUnsavedEdits || _isLiveRecording) return;
        var choice = MessageBox.Show(this, "Save your meeting edits before closing? Choose No to discard them, or Cancel to keep editing.",
            "Unsaved meeting edits", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        e.Cancel = choice == MessageBoxResult.Cancel || choice == MessageBoxResult.Yes && !TrySaveEdits();
    }

    private void Timestamp_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is double seconds) NavigateToTimestamp(seconds);
    }

    private void Markdown_TimestampRequested(object? sender, TimestampNavigationEventArgs e) => NavigateToTimestamp(e.Seconds);

    internal void NavigateToTimestamp(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return;
        var lines = _meeting.Transcript.Where(line => double.IsFinite(line.StartSeconds)).ToList();
        var line = lines.Where(line => line.StartSeconds <= seconds && seconds <= line.EndSeconds)
            .OrderByDescending(line => line.StartSeconds).FirstOrDefault()
            ?? lines.OrderBy(line => Math.Abs(line.StartSeconds - seconds)).FirstOrDefault();
        if (line is null)
        {
            SaveStatus.Text = "No transcript at this time yet. Try again when transcription finishes.";
            return;
        }
        ReviewTabs.SelectedItem = TranscriptTab;
        SaveStatus.Text = $"Transcript at {TimeSpan.FromSeconds(line.StartSeconds):hh\\:mm\\:ss}" +
            (HasUnsavedEdits ? " · unsaved changes" : " · saved locally");
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            TranscriptList.UpdateLayout();
            if (TranscriptList.ItemContainerGenerator.ContainerFromItem(line) is not DependencyObject container) return;
            var border = FindTranscriptBorder(container);
            border?.BringIntoView();
            border?.Focus();
        }));
    }

    private static Border? FindTranscriptBorder(DependencyObject parent)
    {
        if (parent is Border { Focusable: true } border) return border;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindTranscriptBorder(VisualTreeHelper.GetChild(parent, i)) is { } found) return found;
        return null;
    }
}
