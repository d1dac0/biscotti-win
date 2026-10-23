using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Biscotti.Core;
using Biscotti.Core.Models;
using Biscotti.Win;
using Xunit;

namespace Biscotti.Win.Tests;

// Run on the existing suite's STA/Application (issue #120): tag filter, tag
// editing with immediate save, and cross-meeting rename/delete — all on the
// design-preview fixture, without real meeting storage.
internal static class TagChecks
{
    internal static async Task Run(MainWindow main)
    {
        main.ConfigureDesignPreview("history");
        var filter = (ComboBox)main.FindName("TagFilterBox");
        var manage = (Button)main.FindName("ManageTagsButton");
        var search = (TextBox)main.FindName("SearchBox");
        var list = (ListBox)main.FindName("MeetingList");
        Assert.True(filter.IsEnabled);
        Assert.True(manage.IsEnabled);
        Assert.Equal(3, filter.Items.Count); // All tags + Atlas + Planning from the fixture
        filter.SelectedItem = filter.Items.OfType<MainWindow.TagFilterChoice>().Single(c => c.Tag == "Atlas");
        await main.ApplyHistorySearchAsync(debounce: false);
        Assert.Single(list.Items);
        var row = Assert.IsType<MeetingHistoryResult>(list.Items[0]!);
        Assert.Equal("Tags: Atlas · Planning", row.TagsLine);

        search.Text = "jordan"; // search and tag filter compose
        await main.ApplyHistorySearchAsync(debounce: false);
        Assert.Single(list.Items);
        search.Text = "missing phrase";
        await main.ApplyHistorySearchAsync(debounce: false);
        Assert.Empty(list.Items);
        Assert.Contains("tag", ((TextBlock)main.FindName("HistoryStatus")).Text, StringComparison.OrdinalIgnoreCase);
        search.Clear();
        filter.SelectedIndex = 0;
        await main.ApplyHistorySearchAsync(debounce: false);
        Assert.Single(list.Items);

        var meeting = DesignPreview.CreateMeeting();
        var fail = false;
        Meeting? savedDraft = null;
        var window = new MeetingWindow(meeting, designPreview: true, saveMeeting: draft =>
        {
            if (fail) throw new IOException("Synthetic disk failure");
            savedDraft = draft;
        });
        window.Show();
        Flush(window);
        var chips = (ItemsControl)window.FindName("TagChips");
        var newTag = (TextBox)window.FindName("NewTagBox");
        var addTag = (Button)window.FindName("AddTagButton");
        var tagStatus = (TextBlock)window.FindName("TagStatus");
        Assert.Equal(2, chips.Items.Count);
        Assert.False(addTag.IsEnabled);
        newTag.Text = "  Client  Acme  ";
        Assert.True(addTag.IsEnabled);
        addTag.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(3, meeting.Tags.Count);
        Assert.Equal("Client Acme", meeting.Tags[2]);
        Assert.Same(meeting, savedDraft); // immediate save, whole meeting
        Assert.Contains("Added", tagStatus.Text);
        Assert.False(window.HasUnsavedEdits); // tag saves never dirty the editor

        newTag.Text = "atlas"; // duplicate rejected case-insensitively
        addTag.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(3, meeting.Tags.Count);
        Assert.Contains("already", tagStatus.Text);

        fail = true; // storage failure rolls the tag back
        newTag.Text = "Rollback me";
        addTag.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(3, meeting.Tags.Count);
        Assert.Contains("not saved", tagStatus.Text);
        fail = false;

        window.RemoveTag_Click(new Button { Tag = "Planning" }, new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { "Atlas", "Client Acme" }, meeting.Tags);
        Assert.Contains("Removed", tagStatus.Text);
        window.Close();

        var live = new MeetingWindow(DesignPreview.CreateMeeting(), isLiveRecording: true, designPreview: true);
        live.Show();
        Flush(live);
        Assert.False(((StackPanel)live.FindName("TagsPanel")).IsEnabled);
        live.Close();

        var first = DesignPreview.CreateMeeting();
        var second = new Meeting { Id = Guid.NewGuid(), Title = "Second standup", Tags = new() { "ATLAS" } };
        var savedIds = new List<Guid>();
        var manager = new TagsWindow(new List<Meeting> { first, second },
            m => savedIds.Add(m.Id), (_, _) => true);
        manager.Show();
        Flush(manager);
        var tagsList = (ListBox)manager.FindName("TagsList");
        Assert.Equal(2, tagsList.Items.Count); // Atlas (2) merged case-insensitively, Planning (1)
        tagsList.SelectedItem = tagsList.Items.OfType<MeetingTags.TagSummary>().Single(t => t.Count == 2);
        Assert.Equal("Atlas", ((MeetingTags.TagSummary)tagsList.SelectedItem).Tag);
        ((TextBox)manager.FindName("RenameTagBox")).Text = "Atlas project";
        ((Button)manager.FindName("RenameTagButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { "Atlas project", "Planning" }, first.Tags);
        Assert.Equal(new[] { "Atlas project" }, second.Tags);
        Assert.Equal(new[] { first.Id, second.Id }, savedIds);

        tagsList.SelectedItem = tagsList.Items.OfType<MeetingTags.TagSummary>().Single(t => t.Tag == "Planning");
        ((Button)manager.FindName("DeleteTagButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { "Atlas project" }, first.Tags);
        Assert.Contains("Removed", ((TextBlock)manager.FindName("TagStatus")).Text);
        manager.Close();
    }

    private static void Flush(Window window) => window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
}
