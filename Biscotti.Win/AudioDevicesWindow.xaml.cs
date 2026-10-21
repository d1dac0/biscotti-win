using System.Windows;
using Biscotti.Core;
using Biscotti.Core.Audio;

namespace Biscotti.Win;

public partial class AudioDevicesWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<bool> _canEdit;
    private readonly bool _designPreview;
    private string? _pendingOutputId;

    public AudioDevicesWindow(AppSettings settings, Func<bool> canEdit, bool designPreview = false)
    {
        InitializeComponent();
        _settings = settings;
        _canEdit = canEdit;
        _designPreview = designPreview;
        LoadChoices(settings.MicDeviceIndex, settings.MicDeviceName, settings.MicDeviceEndpointId, settings.SpeakerDeviceId);
        SetEditingEnabled(canEdit());
    }

    public void SetEditingEnabled(bool enabled)
    {
        DeviceChoices.IsEnabled = enabled;
        RefreshButton.IsEnabled = enabled;
        SaveButton.IsEnabled = enabled;
        if (!enabled) DeviceMessage.Text = "Finish recording and processing before changing devices.";
    }

    private void LoadChoices(int? micIndex, string? micName, string? micEndpointId, string? outputId)
    {
        _pendingOutputId = outputId;
        try
        {
            var devices = _designPreview
                ? new AudioDeviceSnapshot(new[] { new WaveInDeviceInfo(0, "Synthetic microphone") },
                    new[] { new OutputDeviceChoice(null, "Synthetic speakers") }, "synthetic-default")
                : AudioDevices.Enumerate();
            MicrophoneBox.ItemsSource = devices.Microphones;
            OutputBox.ItemsSource = devices.Outputs;
            MicrophoneBox.SelectedItem = micIndex is null && micName is null
                ? devices.Microphones.FirstOrDefault()
                : devices.Microphones.FirstOrDefault(m => m.Index == micIndex && m.Name == micName
                    && (micEndpointId is null || m.EndpointId == micEndpointId));
            OutputBox.SelectedItem = devices.Outputs.FirstOrDefault(o => o.Id == outputId);
            DeviceMessage.Text = MicrophoneBox.SelectedItem is null || OutputBox.SelectedItem is null
                ? "A saved device is unavailable. Choose an available device, then save."
                : outputId is null && devices.DefaultOutputId is null
                    ? "Windows has no default output. Connect an output device and refresh."
                    : "Devices are checked again when recording starts. Silent system audio is normal when nothing is playing.";
            DeviceMessage.SetResourceReference(ForegroundProperty,
                MicrophoneBox.SelectedItem is null || OutputBox.SelectedItem is null
                || outputId is null && devices.DefaultOutputId is null ? "DangerBrush" : "MutedBrush");
        }
        catch (Exception ex) { ReportError("Could not list audio devices. " + ex.Message); }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEdit()) return;
        var mic = MicrophoneBox.SelectedItem as WaveInDeviceInfo;
        var output = OutputBox.SelectedItem as OutputDeviceChoice;
        LoadChoices(mic?.Index ?? _settings.MicDeviceIndex, mic?.Name ?? _settings.MicDeviceName,
            mic is null ? _settings.MicDeviceEndpointId : mic.EndpointId,
            output is null ? _pendingOutputId : output.Id);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_designPreview) { DialogResult = true; return; }
        if (!_canEdit()) { SetEditingEnabled(false); return; }
        if (MicrophoneBox.SelectedItem is not WaveInDeviceInfo mic || OutputBox.SelectedItem is not OutputDeviceChoice output)
        {
            ReportError("Choose both a microphone and an output device.");
            return;
        }
        try
        {
            var choice = new AppSettings { MicDeviceIndex = mic.Index, MicDeviceName = mic.Name,
                MicDeviceEndpointId = mic.EndpointId, SpeakerDeviceId = output.Id };
            AudioDevices.Validate(choice, AudioDevices.Enumerate());
            var previous = (_settings.MicDeviceIndex, _settings.MicDeviceName,
                _settings.MicDeviceEndpointId, _settings.SpeakerDeviceId);
            try
            {
                (_settings.MicDeviceIndex, _settings.MicDeviceName, _settings.MicDeviceEndpointId, _settings.SpeakerDeviceId) =
                    (mic.Index, mic.Name, mic.EndpointId, output.Id);
                _settings.Save();
            }
            catch
            {
                (_settings.MicDeviceIndex, _settings.MicDeviceName, _settings.MicDeviceEndpointId, _settings.SpeakerDeviceId) = previous;
                throw;
            }
            DialogResult = true;
        }
        catch (Exception ex) { ReportError("Could not save devices. " + ex.Message); }
    }

    private void ReportError(string message)
    {
        DeviceMessage.Text = message;
        DeviceMessage.SetResourceReference(ForegroundProperty, "DangerBrush");
    }
}
