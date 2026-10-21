using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Biscotti.Core.Calendar;
using Biscotti.Win;
using Xunit;

namespace Biscotti.Win.Tests;

internal static class CalendarChecks
{
    internal static void Run(MainWindow main)
    {
        var ev = DesignPreview.CreateCalendarEvent();
        var invoked = false;
        var stale = new CalendarEventWindow([ev], _ => Task.FromResult<CalendarMeeting?>(null), _ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        stale.Show();
        ((Button)stale.FindName("EventActionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(invoked);
        Assert.Contains("no longer available", ((TextBlock)stale.FindName("EventStatus")).Text);
        stale.Close();

        foreach (var join in new[] { false, true })
        {
            var pending = new TaskCompletionSource<CalendarMeeting?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dismissed = new CalendarEventWindow([ev], _ => pending.Task, _ =>
            {
                invoked = true;
                return Task.CompletedTask;
            }, openConference: _ => invoked = true);
            dismissed.Show();
            var action = dismissed.ActAsync(join);
            dismissed.Close();
            pending.SetResult(ev);
            Await(action);
            Assert.False(invoked);
        }

        var updated = ev with { Description = "Fresh calendar content" };
        CalendarMeeting? used = null;
        var fresh = new CalendarEventWindow([ev], _ => Task.FromResult<CalendarMeeting?>(updated), e =>
        {
            used = e;
            return Task.CompletedTask;
        });
        fresh.Show();
        ((Button)fresh.FindName("EventActionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(updated, used);
        Assert.False(fresh.IsVisible);

        var empty = new CalendarEventWindow([], _ => Task.FromResult<CalendarMeeting?>(null), _ => Task.CompletedTask, linkMode: true);
        empty.Show();
        Assert.False(((Button)empty.FindName("EventActionButton")).IsEnabled);
        Assert.False(((Button)empty.FindName("JoinButton")).IsEnabled);
        Assert.Contains("No calendar events", ((TextBlock)empty.FindName("EventStatus")).Text);
        empty.Close();

        var preview = new CalendarEventWindow([ev], _ => throw new InvalidOperationException("Preview must not query a source"),
            _ => throw new InvalidOperationException("Preview must not record"), designPreview: true);
        preview.Show();
        Assert.True(((Button)preview.FindName("JoinButton")).IsEnabled);
        ((Button)preview.FindName("JoinButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("no meeting link opened", ((TextBlock)preview.FindName("EventStatus")).Text);
        Capture(preview, "event-light");
        ThemePalette.Apply(highContrast: false, darkMode: true);
        Capture(preview, "event-dark");
        ThemePalette.Apply(highContrast: false, darkMode: false);
        preview.Close();

        var connection = new CalendarConnectionWindow(new(), new NeverRead(), designPreview: true);
        connection.Show();
        Assert.False(((Button)connection.FindName("SaveConnectionButton")).IsEnabled);
        Assert.True(((Button)connection.FindName("GoogleButton")).IsEnabled);
        Capture(connection, "connect-calendars");
        connection.Width = connection.MinWidth;
        connection.Height = connection.MinHeight;
        Capture(connection, "connect-narrow");
        connection.Close();
        foreach (var provider in new[] { "google", "microsoft", "icloud" })
        {
            var setup = new CalendarProviderWindow(provider, designPreview: true);
            setup.Show();
            ((Button)setup.FindName("ConnectProviderButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("no provider account contacted", ((TextBlock)setup.FindName("ProviderStatus")).Text);
            Assert.Equal(provider == "icloud" ? Visibility.Visible : Visibility.Collapsed, ((StackPanel)setup.FindName("AppleFields")).Visibility);
            Capture(setup, $"provider-{provider}");
            setup.Close();
        }

        var meeting = DesignPreview.CreateMeeting();
        meeting.CalendarEvent = ev;
        meeting.CalendarAttendees = ev.Attendees.ToList();
        var failSave = true;
        var review = new MeetingWindow(meeting, designPreview: true, saveMeeting: _ =>
        {
            if (failSave) throw new IOException("Synthetic save failure");
        }, calendarEventsNear: _ => Task.FromResult<IReadOnlyList<CalendarMeeting>>([ev]));
        review.Show();
        ((Button)review.FindName("RemoveCalendarLinkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(ev, meeting.CalendarEvent);
        Assert.Contains("previous calendar link", ((TextBlock)review.FindName("CalendarLinkStatus")).Text);
        var originalTitle = meeting.Title;
        var originalSpeaker = meeting.Transcript[0].Speaker;
        failSave = false;
        review.SetCalendarAssociation(ev with { Title = "Corrected calendar event" });
        Assert.Equal("Corrected calendar event", ((TextBlock)review.FindName("CalendarContextTitle")).Text);
        Capture(review, "linked-meeting");
        ((Button)review.FindName("RemoveCalendarLinkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(meeting.CalendarEvent);
        Assert.Empty(meeting.CalendarAttendees);
        Assert.Equal(originalTitle, meeting.Title);
        Assert.Equal(originalSpeaker, meeting.Transcript[0].Speaker);
        review.Close();

        var liveMeeting = DesignPreview.CreateMeeting();
        var live = new MeetingWindow(liveMeeting, isLiveRecording: true, designPreview: true,
            calendarEventsNear: _ => Task.FromResult<IReadOnlyList<CalendarMeeting>>([ev]),
            setCalendarEvent: e => CalendarAssociation.Set(liveMeeting, e, _ => { }));
        live.Show();
        live.SetCalendarAssociation(ev);
        Assert.Same(ev, liveMeeting.CalendarEvent);
        live.StopAddingTimestampedNotes();
        Assert.False(((Button)live.FindName("RemoveCalendarLinkButton")).IsEnabled);
        Assert.Throws<InvalidOperationException>(() => live.SetCalendarAssociation(null));
        live.Close();

        main.ConfigureDesignPreview("calendar");
        main.UpdateLayout();
        var dashboard = (Grid)main.FindName("DashboardGrid");
        var primary = (StackPanel)main.FindName("MainPrimaryPanel");
        Assert.True(primary.ActualWidth <= dashboard.ColumnDefinitions[2].ActualWidth + 1);
        var refreshApps = (Button)main.FindName("RefreshAppsButton");
        var bounds = refreshApps.TransformToAncestor(primary).TransformBounds(new Rect(refreshApps.RenderSize));
        Assert.True(bounds.Right <= primary.ActualWidth + 1);
        Capture(main, "upcoming-main");
        var width = main.Width;
        var height = main.Height;
        main.Width = main.MinWidth;
        main.Height = main.MinHeight;
        Capture(main, "upcoming-narrow");
        main.Width = width;
        main.Height = height;
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("BISCOTTI_CALENDAR_PREVIEWS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        var root = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth + root.Margin.Left + root.Margin.Right),
            (int)Math.Ceiling(root.ActualHeight + root.Margin.Top + root.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }

    private static void Await(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }

    private sealed class NeverRead : ICalendarSource
    {
        public Task<CalendarSchedule> ReadAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default) =>
            throw new InvalidOperationException("Synthetic preview must not access calendars");
    }
}
