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
                main.ConfigureDesignPreview("processing");
                Await(main.ApplyHistorySearchAsync(debounce: false));
                Assert.False(((Button)main.FindName("RecordButton")).IsEnabled);
                Assert.Equal(Visibility.Visible, ((ProgressBar)main.FindName("ProcessingProgress")).Visibility);
                var devices = new AudioDevicesWindow(new Biscotti.Core.AppSettings(), () => true, designPreview: true);
                Assert.Single(((ComboBox)devices.FindName("MicrophoneBox")).Items);
                Assert.Single(((ComboBox)devices.FindName("OutputBox")).Items);
                devices.SetEditingEnabled(false);
                Assert.False(((Button)devices.FindName("SaveButton")).IsEnabled);
                devices.Close();

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
                Assert.Equal(meeting.Title, ((TextBox)review.FindName("TitleBox")).Text);
                Assert.Equal(meeting.Summary, ((TextBox)review.FindName("SummaryBox")).Text);
                Assert.Equal(Visibility.Collapsed, ((TextBlock)review.FindName("TranscriptEmptyText")).Visibility);
                Assert.Equal(Visibility.Visible, ((GroupBox)review.FindName("ActionItemsGroup")).Visibility);
                Assert.Single(((ItemsControl)review.FindName("ActionItemsList")).Items);
                Assert.Same(review.FindName("SummaryTab"), ((TabControl)review.FindName("ReviewTabs")).SelectedItem);
                review.Close();
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
