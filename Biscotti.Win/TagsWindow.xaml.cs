using System.Windows;
using System.Windows.Controls;
using Biscotti.Core;
using Biscotti.Core.Models;

namespace Biscotti.Win;

/// <summary>
/// Cross-meeting tag management (#120): rename or delete a tag on every
/// meeting that carries it. A null save callback (design preview) keeps
/// changes in memory only.
/// </summary>
public partial class TagsWindow : Window
{
    private readonly IReadOnlyList<Meeting> _meetings;
    private readonly Action<Meeting>? _save;
    private readonly Func<string, int, bool>? _confirmDelete;

    public TagsWindow(IReadOnlyList<Meeting> meetings, Action<Meeting>? save,
        Func<string, int, bool>? confirmDelete = null)
    {
        InitializeComponent();
        _meetings = meetings;
        _save = save;
        _confirmDelete = confirmDelete;
        RefreshList();
    }

    private void RefreshList()
    {
        TagsList.ItemsSource = MeetingTags.Summarize(_meetings);
        if (TagsList.Items.Count > 0) TagsList.SelectedIndex = 0;
        TagStatus.Text = TagsList.Items.Count == 0
            ? "No tags yet. Tag a meeting from its review window first."
            : "";
        UpdateButtons();
    }

    private void TagsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        var selected = TagsList.SelectedItem is MeetingTags.TagSummary;
        RenameTagButton.IsEnabled = selected;
        DeleteTagButton.IsEnabled = selected;
    }

    private void RenameTag_Click(object sender, RoutedEventArgs e)
    {
        if (TagsList.SelectedItem is not MeetingTags.TagSummary selected) return;
        if (string.Equals(MeetingTags.Normalize(RenameTagBox.Text), selected.Tag, StringComparison.OrdinalIgnoreCase))
        {
            TagStatus.Text = "The new name matches the current tag.";
            return;
        }
        var affected = MeetingTags.RenameAll(_meetings, selected.Tag, RenameTagBox.Text);
        if (affected.Count == 0)
        {
            TagStatus.Text = "Enter a new tag name to rename.";
            return;
        }
        var failures = SaveAffected(affected);
        var renamed = MeetingTags.Normalize(RenameTagBox.Text)!;
        RenameTagBox.Clear();
        RefreshList();
        TagStatus.Text = failures == 0
            ? $"Renamed to '{renamed}' on {affected.Count} {(affected.Count == 1 ? "meeting" : "meetings")}."
            : $"Renamed to '{renamed}', but {failures} of {affected.Count} {(failures == 1 ? "save failed" : "saves failed")}. Reopen this window to retry.";
    }

    private void DeleteTag_Click(object sender, RoutedEventArgs e)
    {
        if (TagsList.SelectedItem is not MeetingTags.TagSummary selected) return;
        var confirmed = _confirmDelete?.Invoke(selected.Tag, selected.Count)
            ?? MessageBox.Show(this,
                $"Remove the tag '{selected.Tag}' from {selected.Count} {(selected.Count == 1 ? "meeting" : "meetings")}?",
                "Remove tag", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        if (!confirmed) return;
        var affected = MeetingTags.RemoveAll(_meetings, selected.Tag);
        var failures = SaveAffected(affected);
        RefreshList();
        TagStatus.Text = failures == 0
            ? $"Removed '{selected.Tag}' from {affected.Count} {(affected.Count == 1 ? "meeting" : "meetings")}."
            : $"Removed from {affected.Count - failures}, but {failures} {(failures == 1 ? "save failed" : "saves failed")}. Reopen this window to retry.";
    }

    private int SaveAffected(IReadOnlyList<Meeting> affected)
    {
        var failures = 0;
        foreach (var meeting in affected)
        {
            try { _save?.Invoke(meeting); }
            catch { failures++; }
        }
        return failures;
    }
}
