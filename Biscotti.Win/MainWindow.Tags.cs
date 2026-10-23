using System.Windows;
using System.Windows.Controls;
using Biscotti.Core;
using Biscotti.Core.Storage;

namespace Biscotti.Win;

public partial class MainWindow
{
    private bool _updatingTagFilter;

    internal sealed record TagFilterChoice(string? Tag, int Count)
    {
        public override string ToString() => Tag is null ? "All tags" : $"{Tag} ({Count})";
    }

    internal string? SelectedTagFilter => (TagFilterBox.SelectedItem as TagFilterChoice)?.Tag;

    /// <summary>Rebuilds the filter choices from current history. Keeps a still-existing selection.</summary>
    private void RefreshTagFilterChoices()
    {
        if (TagFilterBox is null) return;
        _updatingTagFilter = true;
        try
        {
            var previous = SelectedTagFilter;
            var choices = new List<TagFilterChoice> { new(null, 0) };
            choices.AddRange(MeetingTags.Summarize(_historyDocuments.Select(d => d.Meeting))
                .Select(s => new TagFilterChoice(s.Tag, s.Count)));
            TagFilterBox.ItemsSource = choices;
            TagFilterBox.SelectedItem = previous is null ? choices[0]
                : choices.FirstOrDefault(c => string.Equals(c.Tag, previous, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
            TagFilterBox.IsEnabled = _historyReady && choices.Count > 1;
            ManageTagsButton.IsEnabled = _historyReady;
        }
        finally { _updatingTagFilter = false; }
    }

    private async void TagFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingTagFilter) return;
        await ApplyHistorySearchAsync();
    }

    private void ManageTags_Click(object sender, RoutedEventArgs e)
    {
        var meetings = _historyDocuments.Select(d => d.Meeting).ToList();
        if (meetings.Count == 0) return;
        var dialog = new TagsWindow(meetings, _designPreview ? null : MeetingStore.Save) { Owner = this };
        dialog.ShowDialog();
        // The manager mutates meetings in place and saves each one; reload from
        // protected storage so the list and filter choices match what is on disk.
        if (_designPreview) SetHistory(meetings);
        else RefreshList();
    }
}
