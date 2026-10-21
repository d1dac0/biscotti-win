using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Biscotti.Core;
using Biscotti.Win;
using Xunit;

namespace Biscotti.Win.Tests;

public sealed class MeetingFlowTests
{
    // WPF needs STA. Keep all window checks on one thread and one Application;
    // no audio hardware, models, profile settings or storage are opened.
    [Fact]
    public void PreviewAndMeetingReviewKeepSetupLiveAndCompletedStatesDistinct()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/Biscotti.Win;component/Theme.xaml", UriKind.Relative),
                });
                var regularPaper = app.Resources["PaperBrush"];
                ThemePalette.Apply(highContrast: true);
                Assert.Same(SystemColors.WindowBrush, app.Resources["PaperBrush"]);
                Assert.Same(SystemColors.WindowTextBrush, app.Resources["MutedBrush"]);
                Assert.Same(SystemColors.HighlightTextBrush, app.Resources["AccentTextBrush"]);
                ThemePalette.Apply(highContrast: false);
                Assert.Same(regularPaper, app.Resources["PaperBrush"]);
                var main = new MainWindow(designPreview: true);
                main.UpdateAutoStopNotice(new Biscotti.Core.Audio.AutoStopNotice(true, true, false, 30));
                Assert.Equal(Visibility.Visible, ((Border)main.FindName("AutoStopBanner")).Visibility);
                main.Show();
                main.Hide(); // tray close/reopen does not recreate or reset the notice
                main.Show();
                Assert.Contains("30 seconds", ((TextBlock)main.FindName("AutoStopCountdownText")).Text);
                ((Button)main.FindName("KeepRecordingButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Collapsed, ((Border)main.FindName("AutoStopBanner")).Visibility);
                Assert.Equal("", ((TextBlock)main.FindName("AutoStopCountdownText")).Text);
                main.UpdateAutoStopNotice(new Biscotti.Core.Audio.AutoStopNotice(true, true, false, 12));
                main.UpdateRecordingPresentationForCountdownPreview();
                main.TogglePreviewRecording(); // stopping clears the pending notice too
                Assert.Equal(Visibility.Collapsed, ((Border)main.FindName("AutoStopBanner")).Visibility);
                Assert.Equal("", ((TextBlock)main.FindName("AutoStopCountdownText")).Text);
                main.ConfigureDesignPreview("setup");
                Await(main.ApplyHistorySearchAsync(debounce: false));
                Assert.False(((Button)main.FindName("RecordButton")).IsEnabled);
                Assert.Equal(Visibility.Visible, ((Button)main.FindName("ModelButton")).Visibility);
                Assert.Contains("No meetings yet", ((TextBlock)main.FindName("HistoryStatus")).Text);
                main.ConfigureDesignPreview("history");
                Await(main.ApplyHistorySearchAsync(DesignPreview.CreateMeeting().Id, debounce: false));
                Assert.True(((Button)main.FindName("RecordButton")).IsEnabled);
                Assert.Equal(Visibility.Collapsed, ((Button)main.FindName("ModelButton")).Visibility);
                Assert.True(((Button)main.FindName("OpenMeetingButton")).IsEnabled);
                Assert.True(((Button)main.FindName("ImportCsvButton")).IsEnabled);
                Assert.True(((Button)main.FindName("ExportCsvButton")).IsEnabled);
                ((Button)main.FindName("ExportCsvButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("unencrypted CSV", ((TextBlock)main.FindName("TransferStatus")).Text);
                var search = (TextBox)main.FindName("SearchBox");
                var list = (ListBox)main.FindName("MeetingList");
                search.Text = "keyboard navigation";
                Await(main.ApplyHistorySearchAsync(debounce: false));
                Assert.Contains("Note:", Assert.IsType<MeetingHistoryResult>(Assert.Single(list.Items)).Snippet);
                Assert.Null(list.Items.Groups); // relevance order has no date groups
                search.Text = "missing phrase";
                var superseded = main.ApplyHistorySearchAsync();
                search.Text = "atlas";
                Await(main.ApplyHistorySearchAsync(debounce: false));
                Await(superseded);
                Assert.Single(list.Items);
                search.Text = "missing phrase";
                Await(main.ApplyHistorySearchAsync(debounce: false));
                Assert.Empty(list.Items);
                Assert.Contains("No matching", ((TextBlock)main.FindName("HistoryStatus")).Text);
                ((Button)main.FindName("ClearSearchButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Await(main.ApplyHistorySearchAsync(debounce: false));
                Assert.Equal("", search.Text);
                Assert.Single(list.Items);
                Assert.Single(list.Items.Groups!);
                main.ConfigureDesignPreview("failure");
                search.Text = "atlas";
                Await(main.ApplyHistorySearchAsync(debounce: false));
                Assert.Empty(list.Items);
                Assert.Contains("Protected data", ((TextBlock)main.FindName("HistoryStatus")).Text);
                Assert.False(((Button)main.FindName("OpenMeetingButton")).IsEnabled);
                Assert.False(((Button)main.FindName("ImportCsvButton")).IsEnabled);
                Assert.False(((Button)main.FindName("ExportCsvButton")).IsEnabled);
                main.ConfigureDesignPreview("processing");
                Await(main.ApplyHistorySearchAsync(debounce: false));
                Assert.False(((Button)main.FindName("RecordButton")).IsEnabled);
                Assert.False(((Button)main.FindName("ImportCsvButton")).IsEnabled);
                Assert.Equal(Visibility.Visible, ((ProgressBar)main.FindName("ProcessingProgress")).Visibility);
                var devices = new AudioDevicesWindow(new Biscotti.Core.AppSettings(), () => true, designPreview: true);
                Assert.Single(((ComboBox)devices.FindName("MicrophoneBox")).Items);
                Assert.Single(((ComboBox)devices.FindName("OutputBox")).Items);
                devices.SetEditingEnabled(false);
                Assert.False(((Button)devices.FindName("SaveButton")).IsEnabled);
                devices.Close();

                var original = new Biscotti.Core.AppSettings();
                var saved = false;
                var settings = new SettingsWindow(original, _ => { saved = true; return null; }, designPreview: true);
                Assert.True(((CheckBox)settings.FindName("AutoStopBox")).IsChecked);
                ((CheckBox)settings.FindName("AutoStopBox")).IsChecked = false;
                settings.Show();
                settings.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
                Assert.Equal(2, ((ComboBox)settings.FindName("WhisperBox")).Items.Count);
                Assert.Single(((ComboBox)settings.FindName("OllamaBox")).Items);
                Assert.Contains("ready", ((TextBlock)settings.FindName("OllamaStatus")).Text);
                ((TextBox)settings.FindName("GuidanceBox")).Text = new string('x', 4001);
                ((Button)settings.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(saved);
                Assert.Contains(4000.ToString("N0"), ((TextBlock)settings.FindName("MessageText")).Text);
                settings.Close();
                Assert.Null(original.SummaryGuidance);
                Assert.Equal("standard", original.SummaryLength);
                Assert.True(original.AutoStopEnabled); // cancelling leaves preference unchanged

                Biscotti.Core.AppSettings? savedDraft = null;
                var validSettings = new SettingsWindow(original, draft => { savedDraft = draft; return null; }, designPreview: true);
                validSettings.Loaded += (_, _) => validSettings.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ((ComboBox)validSettings.FindName("SummaryLengthBox")).SelectedItem = "brief";
                    ((CheckBox)validSettings.FindName("AutoStopBox")).IsChecked = false;
                    ((TextBox)validSettings.FindName("GuidanceBox")).Text = "Focus on decisions.";
                    ((System.Windows.Controls.CheckBox)validSettings.FindName("CalendarNotificationsBox")).IsChecked = true;
                    ((System.Windows.Controls.CheckBox)validSettings.FindName("CalendarTrayBox")).IsChecked = false;
                    ((Button)validSettings.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Assert.True(validSettings.ShowDialog());
                Assert.Equal("brief", savedDraft!.SummaryLength);
                Assert.False(savedDraft.AutoStopEnabled);
                Assert.Equal("Focus on decisions.", savedDraft.SummaryGuidance);
                Assert.True(savedDraft.CalendarStartNotifications);
                Assert.False(savedDraft.CalendarTrayNavigation);
                Assert.Null(original.SummaryGuidance);

                var meeting = DesignPreview.CreateMeeting();
                meeting.Summary = null;
                meeting.ActionItems.Clear();
                meeting.Transcript.Clear();
                var review = new MeetingWindow(meeting, isLiveRecording: true, designPreview: true);
                review.Measure(new Size(940, 760));
                review.Arrange(new Rect(0, 0, 940, 760));
                review.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
                Assert.False(((Button)review.FindName("SaveButton")).IsEnabled);
                Assert.False(((TabItem)review.FindName("AudioTab")).IsEnabled);
                Assert.False(((Button)review.FindName("CopyMarkdownButton")).IsEnabled);
                Assert.False(((Button)review.FindName("ExportMarkdownButton")).IsEnabled);
                Assert.False(((TabItem)review.FindName("SpeakersTab")).IsEnabled);
                Assert.Contains("Listening", ((TextBlock)review.FindName("TranscriptEmptyText")).Text);
                Assert.Equal(Visibility.Collapsed, ((GroupBox)review.FindName("ActionItemsGroup")).Visibility);
                review.StopAddingTimestampedNotes();
                Assert.Contains("Processing", ((TextBlock)review.FindName("LiveStatus")).Text);

                // The model is not INotifyPropertyChanged: completion must refresh
                // the live window's bindings, including a replaced action-item list.
                meeting.Title = "Atlas — complete";
                meeting.Summary = "A completed synthetic summary";
                meeting.ActionItems = new() { "Alex: send the checklist." };
                meeting.Transcript.Add(new() { Speaker = "Alex", Text = "Ready.", StartSeconds = 1 });
                review.SetRecordingComplete();
                review.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
                Assert.True(((Button)review.FindName("SaveButton")).IsEnabled);
                Assert.True(((TabItem)review.FindName("AudioTab")).IsEnabled);
                Assert.True(((Button)review.FindName("CopyMarkdownButton")).IsEnabled);
                ((Button)review.FindName("CopyMarkdownButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("unencrypted Markdown", ((TextBlock)review.FindName("TransferStatus")).Text);
                ((Button)review.FindName("ExportMarkdownButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("unencrypted Markdown file", ((TextBlock)review.FindName("TransferStatus")).Text);
                Assert.True(((TabItem)review.FindName("SpeakersTab")).IsEnabled);
                Assert.Equal(meeting.Title, ((TextBox)review.FindName("TitleBox")).Text);
                Assert.Equal(meeting.Summary, ((TextBox)review.FindName("SummaryBox")).Text);
                Assert.Equal(Visibility.Collapsed, ((TextBlock)review.FindName("TranscriptEmptyText")).Visibility);
                Assert.Equal(Visibility.Visible, ((GroupBox)review.FindName("ActionItemsGroup")).Visibility);
                Assert.Single(((ItemsControl)review.FindName("ActionItemsList")).Items);
                Assert.Same(review.FindName("SummaryTab"), ((TabControl)review.FindName("ReviewTabs")).SelectedItem);
                review.Close();

                var importPreview = new Biscotti.Core.Storage.ImportPreview(new[] { DesignPreview.CreateMeeting() }, 2,
                    new[] { "Row 4: invalid fields, dates or timeline. Check the CSV format." });
                var importDialog = new ImportPreviewWindow(importPreview);
                Assert.Contains("1 ready to import", ((TextBlock)importDialog.FindName("CountsText")).Text);
                Assert.Contains("2 duplicates skipped", ((TextBlock)importDialog.FindName("CountsText")).Text);
                Assert.Contains("1 invalid rows", ((TextBlock)importDialog.FindName("CountsText")).Text);
                Assert.Single(((ListBox)importDialog.FindName("ErrorsList")).Items);
                importDialog.Loaded += (_, _) => importDialog.Dispatcher.BeginInvoke(new Action(() =>
                    ((Button)importDialog.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
                Assert.False(importDialog.ShowDialog());
                var emptyImport = new ImportPreviewWindow(new Biscotti.Core.Storage.ImportPreview(
                    Array.Empty<Biscotti.Core.Models.Meeting>(), 1, Array.Empty<string>()));
                Assert.False(((Button)emptyImport.FindName("ImportButton")).IsEnabled);
                emptyImport.Close();
                var speakersMeeting = DesignPreview.CreateMeeting();
                speakersMeeting.Transcript[0].SpeakerKey = "system:0";
                speakersMeeting.Transcript[1].SpeakerKey = "system:1";
                speakersMeeting.Transcript[2].SpeakerKey = "system:0";
                var speakers = new MeetingWindow(speakersMeeting, designPreview: true);
                speakers.Show();
                ((TabControl)speakers.FindName("ReviewTabs")).SelectedItem = speakers.FindName("SpeakersTab");
                Assert.Equal(2, ((ComboBox)speakers.FindName("SpeakerGroupBox")).Items.Count);
                Assert.False(((CheckBox)speakers.FindName("SpeakerMemoryEnabled")).IsChecked);
                Assert.False(((Button)speakers.FindName("AnalyzeVoiceButton")).IsEnabled);
                ((TextBox)speakers.FindName("SpeakerNameBox")).Text = "Corrected Alex";
                ((Button)speakers.FindName("CorrectSpeakerButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("Corrected Alex", speakersMeeting.Transcript[0].Speaker);
                Assert.Equal("Corrected Alex", speakersMeeting.Transcript[2].Speaker);
                Assert.Equal("Jordan", speakersMeeting.Transcript[1].Speaker);
                var optIn = (CheckBox)speakers.FindName("SpeakerMemoryEnabled");
                optIn.IsChecked = true;
                optIn.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                Assert.True(((Button)speakers.FindName("AnalyzeVoiceButton")).IsEnabled);
                Await(speakers.AnalyzeSelectedVoiceAsync());
                Assert.Contains("No confident suggestion", ((TextBlock)speakers.FindName("SpeakerSuggestionText")).Text);
                Assert.False(((Button)speakers.FindName("UseSuggestionButton")).IsEnabled);
                Assert.True(((Button)speakers.FindName("RememberVoiceButton")).IsEnabled);
                ((Button)speakers.FindName("RememberVoiceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("Preview only", ((TextBlock)speakers.FindName("SpeakerStatus")).Text);
                ((Button)speakers.FindName("ForgetAllButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(optIn.IsChecked);
                Assert.False(((Button)speakers.FindName("RememberVoiceButton")).IsEnabled);
                Assert.Equal("Corrected Alex", speakersMeeting.Transcript[0].Speaker);
                speakers.Close();
                main.Close();
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF state checks did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Await(Task task)
    {
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => frame.Continue = false;
        timeout.Start();
        task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
        if (!task.IsCompleted) Dispatcher.PushFrame(frame);
        timeout.Stop();
        Assert.True(task.IsCompleted, "History search did not finish.");
        task.GetAwaiter().GetResult();
    }
}
