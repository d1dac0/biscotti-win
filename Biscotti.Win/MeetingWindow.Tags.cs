using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Biscotti.Core;

namespace Biscotti.Win;

public partial class MeetingWindow
{
    private void InitializeTags()
    {
        TagsPanel.IsEnabled = !_isLiveRecording;
        if (_isLiveRecording) TagStatus.Text = "Tags can be added once the recording finishes.";
    }

    private void RefreshTags()
    {
        TagChips.ItemsSource = null;
        TagChips.ItemsSource = _meeting.Tags;
        if (!_isLiveRecording)
            TagStatus.Text = _meeting.Tags.Count == 0
                ? "Tag this meeting with a project or client to find it later."
                : $"{_meeting.Tags.Count} {(_meeting.Tags.Count == 1 ? "tag" : "tags")} · tags save immediately.";
    }

    private void NewTag_Changed(object sender, TextChangedEventArgs e) =>
        AddTagButton.IsEnabled = !_isLiveRecording && !_isProcessing && MeetingTags.Normalize(NewTagBox.Text) is not null;

    private void NewTag_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddTag_Click(AddTagButton, new RoutedEventArgs(Button.ClickEvent));
    }

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        if (_isLiveRecording || _isProcessing) return;
        if (MeetingTags.Normalize(NewTagBox.Text) is not { } normalized)
        {
            TagStatus.Text = "Enter a tag to add.";
            return;
        }
        if (_meeting.Tags.Any(t => t.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            TagStatus.Text = $"'{normalized}' is already on this meeting.";
            return;
        }
        if (_meeting.Tags.Count >= MeetingTags.MaxPerMeeting)
        {
            TagStatus.Text = $"This meeting already has {MeetingTags.MaxPerMeeting} tags.";
            return;
        }
        if (!MeetingTags.TryAdd(_meeting, normalized)) return;
        try
        {
            _saveMeeting(_meeting);
        }
        catch (Exception ex)
        {
            MeetingTags.Remove(_meeting, normalized);
            TagStatus.Text = $"Tag not saved · not added. Check storage access and try again. {ex.Message}";
            return;
        }
        RefreshTags();
        NewTagBox.Clear();
        TagStatus.Text = $"Added '{_meeting.Tags[^1]}'.";
    }

    // Internal for the WPF checks; normally reached through a tag chip's Remove button.
    internal void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if (_isLiveRecording || _isProcessing || ((Button)sender).Tag is not string tag) return;
        if (!MeetingTags.Remove(_meeting, tag))
        {
            RefreshTags();
            return;
        }
        try
        {
            _saveMeeting(_meeting);
        }
        catch (Exception ex)
        {
            _meeting.Tags.Add(tag);
            TagStatus.Text = $"Tag not removed · kept. Check storage access and try again. {ex.Message}";
            return;
        }
        RefreshTags();
        TagStatus.Text = $"Removed '{tag}'.";
    }
}
