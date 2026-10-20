using System.Windows;
using System.Windows.Media;
using Biscotti.Core;
using Biscotti.Core.Audio;

namespace Biscotti.Win;

public partial class AudioDevicesWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<bool> _canEdit;
    private string? _pendingOutputId;

    public AudioDevicesWindow(AppSettings settings, Func<bool> canEdit)
    {
        InitializeComponent();
        _settings = settings;
        _canEdit = canEdit;
        LoadChoices(settings.MicDeviceIndex, settings.MicDeviceName, settings.SpeakerDeviceId);
        SetEditingEnabled(canEdit());
    }

    public void SetEditingEnabled(bool enabled)
    {
        DeviceChoices.IsEnabled = enabled;
        RefreshButton.IsEnabled = enabled;
        SaveButton.IsEnabled = enabled;
        if (!enabled) DeviceMessage.Text = "Finish recording and processing before changing devices.";
    }

    private void LoadChoices(int? micIndex, string? micName, string? outputId)
    {
        _pendingOutputId = outputId;
        try
        {
            var devices = AudioDevices.Enumerate();
            MicrophoneBox.ItemsSource = devices.Microphones;
            OutputBox.ItemsSource = devices.Outputs;
            MicrophoneBox.SelectedItem = micIndex is null && micName is null
                ? devices.Microphones.FirstOrDefault()
                : devices.Microphones.FirstOrDefault(m => m.Index == micIndex && m.Name == micName);
            OutputBox.SelectedItem = devices.Outputs.FirstOrDefault(o => o.Id == outputId);
            DeviceMessage.Text = MicrophoneBox.SelectedItem is null || OutputBox.SelectedItem is null
                ? "A saved device is unavailable. Choose an available device, then save."
                : outputId is null && devices.DefaultOutputId is null
                    ? "Windows has no default output. Connect an output device and refresh."
                    : "Devices are checked again when recording starts. Silent system audio is normal when nothing is playing.";
            DeviceMessage.Foreground = MicrophoneBox.SelectedItem is null || OutputBox.SelectedItem is null
                || outputId is null && devices.DefaultOutputId is null ? Brushes.Firebrick : Brushes.DimGray;
        }
        catch (Exception ex) { ReportError("Could not list audio devices. " + ex.Message); }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEdit()) return;
        var mic = MicrophoneBox.SelectedItem as WaveInDeviceInfo;
        var output = OutputBox.SelectedItem as OutputDeviceChoice;
        LoadChoices(mic?.Index ?? _settings.MicDeviceIndex, mic?.Name ?? _settings.MicDeviceName,
            output is null ? _pendingOutputId : output.Id);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEdit()) { SetEditingEnabled(false); return; }
        if (MicrophoneBox.SelectedItem is not WaveInDeviceInfo mic || OutputBox.SelectedItem is not OutputDeviceChoice output)
        {
            ReportError("Choose both a microphone and an output device.");
            return;
        }
        try
        {
            var choice = new AppSettings { MicDeviceIndex = mic.Index, MicDeviceName = mic.Name, SpeakerDeviceId = output.Id };
            AudioDevices.Validate(choice, AudioDevices.Enumerate());
            var previous = (_settings.MicDeviceIndex, _settings.MicDeviceName, _settings.SpeakerDeviceId);
            try
            {
                (_settings.MicDeviceIndex, _settings.MicDeviceName, _settings.SpeakerDeviceId) = (mic.Index, mic.Name, output.Id);
                _settings.Save();
            }
            catch
            {
                (_settings.MicDeviceIndex, _settings.MicDeviceName, _settings.SpeakerDeviceId) = previous;
                throw;
            }
            DialogResult = true;
        }
        catch (Exception ex) { ReportError("Could not save devices. " + ex.Message); }
    }

    private void ReportError(string message)
    {
        DeviceMessage.Text = message;
        DeviceMessage.Foreground = Brushes.Firebrick;
    }
}
