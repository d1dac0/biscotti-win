using System.Windows;
using Biscotti.Core.Audio;

namespace Biscotti.Win;

public partial class MainWindow
{
    private bool _countdownVisible;

    internal void UpdateRecordingPresentationForCountdownPreview()
    {
        if (!_designPreview) throw new InvalidOperationException("Preview requires isolated startup.");
        _recording = true;
        UpdateRecordingPresentation(Biscotti.Core.Models.RecordingState.Recording, true);
        UpdateTrayState(Biscotti.Core.Models.RecordingState.Recording);
        RecordButton.Content = "■  Stop & save";
        AudioAppPicker.IsEnabled = AudioDevicesButton.IsEnabled = false;
        StatusText.Text = "Synthetic countdown. No recording, downloads or saved changes.";
    }

    internal void UpdateAutoStopNotice(AutoStopNotice? notice)
    {
        var pending = notice?.SecondsRemaining is not null;
        AutoStopBanner.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        AutoStopCountdownText.Text = pending ? notice!.Description : "";
        if (_keepRecordingMenuItem is not null) _keepRecordingMenuItem.Visible = pending;
        if (pending && !_countdownVisible && !_designPreview)
        {
            ShowFromShell();
            AutoStopBanner.BringIntoView();
        }
        _countdownVisible = pending;
    }

    private void KeepRecording_Click(object sender, RoutedEventArgs e)
    {
        _meetings.KeepRecording();
        UpdateAutoStopNotice(null);
        StatusText.Text = _designPreview ? "Synthetic recording kept; no audio captured." : _meetings.AutoStopStatus;
    }

    private void AutoStopBox_Click(object sender, RoutedEventArgs e)
    {
        var previous = _settings.AutoStopEnabled;
        _settings.AutoStopEnabled = AutoStopBox.IsChecked == true;
        try
        {
            if (!_designPreview) _settings.Save();
            _meetings.SetAutoStopEnabled(_settings.AutoStopEnabled);
            UpdateAutoStopNotice(null);
            StatusText.Text = _settings.AutoStopEnabled
                ? "Auto-stop enabled. New output audio must be heard before it arms."
                : "Auto-stop disabled. Stop recording manually.";
        }
        catch (Exception ex)
        {
            _settings.AutoStopEnabled = previous;
            AutoStopBox.IsChecked = previous;
            StatusText.Text = "Could not save auto-stop preference. " + ex.Message;
        }
    }
}
