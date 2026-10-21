using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using Biscotti.Core.Models;
using Biscotti.Win;
using Xunit;

namespace Biscotti.Win.Tests;

internal static class BrandIconChecks
{
    internal static void Run(MainWindow main)
    {
        var uri = new Uri("pack://application:,,,/Biscotti.Win;component/Assets/biscotti.ico");
        using var stream = Application.GetResourceStream(uri)!.Stream;
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        foreach (var size in new[] { 16, 24, 32, 48, 64, 128, 256 })
            Assert.Contains(decoder.Frames, frame => frame.PixelWidth == size && frame.PixelHeight == size);

        var devices = new AudioDevicesWindow(new(), () => true, designPreview: true);
        var settings = new SettingsWindow(new(), _ => null, designPreview: true);
        var meeting = new MeetingWindow(DesignPreview.CreateMeeting(), designPreview: true);
        var import = new ImportPreviewWindow(new Biscotti.Core.Storage.ImportPreview([], 0, []));
        foreach (var window in new Window[] { main, devices, settings, meeting, import })
        {
            Assert.NotNull(window.Icon);
            Assert.Contains("biscotti.ico", window.Icon.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        devices.Close();
        settings.Close();
        meeting.Close();
        import.Close();

        // Exercise the actual NotifyIcon after its resource stream has closed.
        // No recording, model download, or profile storage is involved.
        main.ShowPreviewTray();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var tray = typeof(MainWindow).GetField("_trayIcon", flags)!.GetValue(main)!;
        var iconProperty = tray.GetType().GetProperty("Icon")!;
        var tooltipProperty = tray.GetType().GetProperty("Text")!;
        var icon = iconProperty.GetValue(tray);
        Assert.NotNull(icon);
        var update = typeof(MainWindow).GetMethod("UpdateTrayState", flags)!;
        foreach (var (state, text) in new[]
        {
            (RecordingState.Idle, "idle"), (RecordingState.Recording, "recording"),
            (RecordingState.Starting, "processing")
        })
        {
            update.Invoke(main, [state]);
            Assert.Same(icon, iconProperty.GetValue(tray));
            Assert.Equal($"Biscotti Win — {text}", tooltipProperty.GetValue(tray));
        }
        update.Invoke(main, [RecordingState.Idle]);
        typeof(MainWindow).GetMethod("DisposeShell", flags)!.Invoke(main, null);
        Assert.Null(typeof(MainWindow).GetField("_biscottiTrayIcon", flags)!.GetValue(main));
    }
}
