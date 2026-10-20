using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Biscotti.Core;
using Biscotti.Core.Ai;
using Biscotti.Core.Transcription;

namespace Biscotti.Win;

public partial class SettingsWindow : Window
{
    private readonly Func<AppSettings, string?> _saveSettings;
    private readonly AppSettings _draft;
    private readonly bool _preview;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _download;
    private OllamaReadiness? _readiness;
    private string _candidate;
    private bool _capturing;

    public SettingsWindow(AppSettings settings, Func<AppSettings, string?> saveSettings,
        string? startupError = null, bool designPreview = false)
    {
        InitializeComponent();
        _draft = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        _preview = designPreview;
        _candidate = settings.RecordingHotkey ?? "Ctrl+Alt+R";
        _saveSettings = saveSettings;
        ShortcutBox.Text = _candidate;
        VocabularyBox.Text = settings.LocalVocabulary ?? "";
        WhisperBox.ItemsSource = ModelStore.All;
        WhisperBox.SelectedItem = ModelStore.All.FirstOrDefault(m => m.Id == settings.WhisperModelId) ?? ModelStore.All[0];
        RuntimeBox.SelectedValue = settings.WhisperRuntime ?? "cpu";
        SummaryLengthBox.ItemsSource = SummaryPreferences.Lengths;
        SummaryLengthBox.SelectedItem = settings.SummaryLength;
        GuidanceBox.Text = settings.SummaryGuidance ?? "";
        Loaded += async (_, _) => await RefreshModelsAsync();
        Closed += (_, _) => { _lifetime.Cancel(); _download?.Cancel(); };
        UpdateWhisperStatus();
        if (!string.IsNullOrWhiteSpace(startupError))
        {
            MessageText.Text = startupError;
            MessageText.SetResourceReference(ForegroundProperty, "DangerBrush");
        }
    }

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        MessageText.Text = "Press a key together with Ctrl, Alt, Shift, or Win. Escape cancels.";
        CaptureButton.Content = "Press keys…";
        Focus();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _capturing = false;
            CaptureButton.Content = "Record shortcut";
            MessageText.Text = "Shortcut capture cancelled.";
            return;
        }

        if (!HotkeyChord.TryCreate(key, Keyboard.Modifiers, out var chord))
        {
            MessageText.Text = "Use a key with at least one modifier. F12 is reserved by Windows.";
            return;
        }

        _candidate = chord.Display;
        ShortcutBox.Text = _candidate;
        MessageText.Text = "Shortcut selected. Save to register it.";
        _capturing = false;
        CaptureButton.Content = "Record shortcut";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _draft.RecordingHotkey = _candidate;
        _draft.LocalVocabulary = VocabularyBox.Text;
        _draft.WhisperModelId = (WhisperBox.SelectedItem as ModelStore.WhisperModel)?.Id;
        _draft.WhisperRuntime = RuntimeBox.SelectedValue as string ?? "cpu";
        _draft.SummaryLength = SummaryLengthBox.SelectedItem as string ?? "";
        _draft.SummaryGuidance = GuidanceBox.Text.Trim();
        if (OllamaBox.SelectedItem is LocalSummaryModel local) _draft.OllamaModel = local.Name;
        var error = new SummaryPreferences(_draft.SummaryLength, _draft.SummaryGuidance).Validate();
        error ??= _saveSettings(_draft);
        if (error is not null)
        {
            MessageText.Text = error;
            MessageText.SetResourceReference(ForegroundProperty, "DangerBrush");
            return;
        }
        DialogResult = true;
    }

    private void Model_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateWhisperStatus();

    private void UpdateWhisperStatus()
    {
        if (WhisperStatus is null || WhisperBox.SelectedItem is not ModelStore.WhisperModel model) return;
        if (_preview) { WhisperStatus.Text = "Synthetic preview: model not downloaded · 20.0 GB free"; return; }
        try
        {
            WhisperStatus.Text = (ModelStore.IsDownloaded(model) ? "Ready for offline transcription. " : "Model missing or incomplete. Download to enable recording. ")
                + $"{ModelStore.AvailableDiskBytes() / 1_000_000_000.0:0.0} GB free on the model drive.";
            DownloadButton.IsEnabled = _download is null && !ModelStore.IsDownloaded(model);
        }
        catch (Exception ex) { WhisperStatus.Text = "Cannot check model storage: " + ex.Message; }
    }

    private async void RefreshModels_Click(object sender, RoutedEventArgs e) => await RefreshModelsAsync();
    private async Task RefreshModelsAsync()
    {
        RefreshModelsButton.IsEnabled = false;
        OllamaStatus.Text = "Checking local Ollama models…";
        try
        {
            _readiness = _preview ? new(true, new[] { new LocalSummaryModel("gemma3:4b", 3_300_000_000) })
                : await OllamaModels.CheckAsync(_lifetime.Token);
            var selected = (OllamaBox.SelectedItem as LocalSummaryModel)?.Name ?? _draft.OllamaModel;
            OllamaBox.ItemsSource = _readiness.Models;
            OllamaBox.SelectedItem = _readiness.Models.FirstOrDefault(m => m.Name == selected);
            OllamaStatus.Text = _readiness.Describe(selected);
        }
        catch (OperationCanceledException) { }
        finally { RefreshModelsButton.IsEnabled = true; }
    }

    private void Ollama_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_readiness is not null) OllamaStatus.Text = _readiness.Describe((OllamaBox.SelectedItem as LocalSummaryModel)?.Name ?? _draft.OllamaModel);
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (WhisperBox.SelectedItem is ModelStore.WhisperModel model)
            await DownloadAsync((progress, ct) => ModelStore.EnsureDownloadedAsync(model, progress, ct));
    }

    private async void GpuDownload_Click(object sender, RoutedEventArgs e) =>
        await DownloadAsync(async (progress, ct) =>
        {
            if (ModelStore.AvailableDiskBytes() < 1_000_000_000)
                throw new System.IO.IOException("Free at least 1 GB on the model drive before installing the GPU runtime.");
            await new GpuRuntimeStore().InstallAsync(progress, ct);
        });

    private async Task DownloadAsync(Func<IProgress<(double pct, string phase)>, CancellationToken, Task> operation)
    {
        if (_preview || _download is not null) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _download = cancellation;
        WhisperBox.IsEnabled = DownloadButton.IsEnabled = GpuDownloadButton.IsEnabled = SaveButton.IsEnabled = false;
        CancelDownloadButton.IsEnabled = true;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        try
        {
            await operation(new Progress<(double pct, string phase)>(p =>
            { DownloadProgress.Value = p.pct; WhisperStatus.Text = p.phase; }), cancellation.Token);
            WhisperStatus.Text = "Download complete. GPU runtime changes take effect after restart.";
        }
        catch (OperationCanceledException) { WhisperStatus.Text = "Download cancelled. Retry when ready."; }
        catch (Exception ex) { WhisperStatus.Text = "Download failed: " + ex.Message + " Retry when ready."; }
        finally
        {
            _download = null;
            WhisperBox.IsEnabled = DownloadButton.IsEnabled = GpuDownloadButton.IsEnabled = SaveButton.IsEnabled = true;
            CancelDownloadButton.IsEnabled = false;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e) => _download?.Cancel();

    private void MicrophoneSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_preview) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true }); }
        catch (Exception ex) { MessageText.Text = "Open Windows Settings > Privacy > Microphone. " + ex.Message; }
    }
}
