using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Biscotti.Core.Models;
using Biscotti.Win;
using Xunit;

namespace Biscotti.Win.Tests;

internal static class MarkdownNotesChecks
{
    // Run on the existing suite's STA/Application, without real meeting storage.
    internal static void Run()
    {
        var meeting = DesignPreview.CreateMeeting();
        const string original = "# Original\r\n\r\n**bold** and *italic* and `code`\r\n- a list\r\n> a quote\r\n```\r\n<script>literal</script>\r\n```";
        meeting.Notes = original;
        Meeting? persisted = null;
        bool fail = true;
        var window = new MeetingWindow(meeting, designPreview: true, saveMeeting: draft =>
        {
            if (fail) throw new IOException("Synthetic disk failure");
            persisted = draft;
        });
        window.Show();
        var tabs = (TabControl)window.FindName("ReviewTabs");
        tabs.SelectedItem = window.FindName("NotesTab");
        Flush(window);
        var source = (TextBox)window.FindName("NotesBox");
        var preview = (SafeMarkdownView)window.FindName("NotesPreview");
        var status = (TextBlock)window.FindName("SaveStatus");
        Assert.Equal(original, source.Text);
        Assert.False(window.HasUnsavedEdits);
        Assert.Contains(preview.Document.Blocks.Cast<Paragraph>(), p => p.FontWeight == FontWeights.SemiBold);
        Assert.Contains(preview.Document.Blocks.Cast<Paragraph>().SelectMany(p => p.Inlines.Cast<Inline>()), i => i is Bold);
        Assert.Contains("<script>literal</script>", new TextRange(preview.Document.ContentStart, preview.Document.ContentEnd).Text);
        fail = false;
        Assert.True(window.TrySaveEdits());
        Assert.Equal(original, persisted!.Notes);
        fail = true;
        persisted = null;

        const string edited = "## Decision\n\n**Ship** [go](biscotti://time/39)\n[remote](https://example.invalid/private) ![image](https://example.invalid/image)\n[script](javascript:alert) [file](file:///secret) [bad](biscotti://time/NaN)\n<img src='https://example.invalid/x'>";
        source.Text = edited;
        Assert.Equal(original, meeting.Notes); // drafts do not leak into another save
        Assert.True(window.HasUnsavedEdits);
        Assert.Contains("Unsaved", status.Text);
        var links = preview.Document.Blocks.Cast<Paragraph>().SelectMany(p => p.Inlines.Cast<Inline>()).OfType<Hyperlink>().ToList();
        var link = Assert.Single(links);
        Assert.Null(link.NavigateUri);
        var rendered = new TextRange(preview.Document.ContentStart, preview.Document.ContentEnd).Text;
        Assert.Contains("https://example.invalid/private", rendered);
        Assert.Contains("![image]", rendered);
        Assert.Contains("javascript:", rendered);

        ((Expander)window.FindName("TimestampedNotesExpander")).IsExpanded = true;
        Flush(window);
        var timedEditor = Descendants<TextBox>((ItemsControl)window.FindName("TimestampedNotesList")).Single();
        timedEditor.Text = "**Updated** timestamp note";
        Assert.NotEqual(timedEditor.Text, meeting.TimestampedNotes[0].Text);
        Assert.False(window.TrySaveEdits());
        Assert.Contains("Not saved", status.Text);
        Assert.Equal(edited, source.Text);
        Assert.Equal(original, meeting.Notes);
        Assert.True(window.HasUnsavedEdits);

        fail = false;
        ApplicationCommands.Save.Execute(null, window); // Ctrl+S uses this routed command
        Assert.NotNull(persisted);
        Assert.Equal(edited, persisted!.Notes);
        Assert.Equal(38, persisted.TimestampedNotes[0].StartSeconds);
        Assert.Equal("**Updated** timestamp note", persisted.TimestampedNotes[0].Text);
        Assert.Equal(meeting.Id, persisted.Id);
        Assert.Equal(meeting.Transcript.Count, persisted.Transcript.Count);
        Assert.False(window.HasUnsavedEdits);
        Assert.Contains("Saved", status.Text);
        Assert.True(window.IsVisible); // Save leaves feedback visible for continued editing

        // Hyperlink's built-in Enter behavior must reach the same local action
        // as a mouse click, select the containing turn, scroll, and focus it.
        Assert.True(link.Focus());
        link.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Enter)
            { RoutedEvent = Keyboard.KeyDownEvent });
        Flush(window);
        Assert.Same(window.FindName("TranscriptTab"), tabs.SelectedItem);
        Assert.Contains("00:00:38", status.Text);
        Assert.IsType<TranscriptLine>(((FrameworkElement)Keyboard.FocusedElement).DataContext);
        Assert.Equal(38, ((TranscriptLine)((FrameworkElement)Keyboard.FocusedElement).DataContext).StartSeconds);

        window.NavigateToTimestamp(0);
        Assert.Contains("00:00:12", status.Text); // before the first turn
        window.NavigateToTimestamp(25);
        Assert.Contains("00:00:24", status.Text); // inside a turn
        window.NavigateToTimestamp(500);
        Assert.Contains("00:00:38", status.Text); // after the last turn
        window.Close();

        var empty = DesignPreview.CreateMeeting();
        empty.Transcript.Clear();
        var emptyWindow = new MeetingWindow(empty, designPreview: true);
        emptyWindow.NavigateToTimestamp(38);
        Assert.Contains("No transcript", ((TextBlock)emptyWindow.FindName("SaveStatus")).Text);
        emptyWindow.Close();

        var liveMeeting = DesignPreview.CreateMeeting();
        var live = new MeetingWindow(liveMeeting, isLiveRecording: true, designPreview: true);
        live.Show();
        ((TabControl)live.FindName("ReviewTabs")).SelectedItem = live.FindName("NotesTab");
        Flush(live);
        Assert.True(Assert.Single(Descendants<TextBox>((ItemsControl)live.FindName("TimestampedNotesList"))).IsReadOnly);
        live.Close();

        Assert.True(SafeMarkdownView.TryGetTimestamp("biscotti://time/12.5", out var seconds));
        Assert.Equal(12.5, seconds);
        foreach (var target in new[] { "biscotti://time/-1", "biscotti://time/Infinity", "biscotti://time/1?url=https://x", "biscotti://time/12,5", "https://x", "file:///x" })
            Assert.False(SafeMarkdownView.TryGetTimestamp(target, out _));
    }

    private static void Flush(Window window) => window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
