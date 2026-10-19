using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using Biscotti.Core;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;
using Biscotti.Core.Transcription;

namespace Biscotti.Win
{
    public partial class MainWindow : Window
    {
        private readonly MeetingService _meetings;
        private readonly AppSettings _settings;
        private bool _recording;
        private MeetingWindow? _liveMeetingWindow;
        private readonly System.Windows.Threading.DispatcherTimer _healthTimer;

        public MainWindow()
        {
            InitializeComponent();
            RefreshAudioApps();
            AudioCaptureHint.Text = Environment.OSVersion.Version.Build < 22000
                ? "Windows 10 captures all speaker audio. Mute notifications and music during the meeting."
                : "Choose the meeting app to capture only its audio; without a selection, all speaker audio is captured.";
            _settings = AppSettings.Load();
            WhisperRuntimeConfigurator.Apply(_settings); // before any WhisperFactory is created
            _meetings = new MeetingService(_settings);
            _meetings.StateChanged += (state, msg) => Dispatcher.Invoke(() => StatusText.Text = msg);
            _meetings.CurrentMeetingUpdated += m => Dispatcher.Invoke(() =>
            {
                RefreshList(m);
                if (_liveMeetingWindow?.IsVisible == true && _liveMeetingWindow.DataContext is Meeting live && live.Id == m.Id)
                    _liveMeetingWindow.RefreshTranscript();
            });
            RefreshList();
            UpdateModelButtons();
            var model = CurrentOrPreferredModel();
            if (!ModelStore.IsDownloaded(model) && System.IO.File.Exists(ModelStore.ModelPath(model)))
            {
                // issue #32: present-but-truncated file — Download is the repair path
                StatusText.Text = "Model file incomplete or corrupt — press Download model to repair.";
            }
            _healthTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _healthTimer.Tick += (_, _) => UpdateRecordingHealth();
            MaybeFirstRunGpuPrompt();
        }

        private bool _gpuPromptHandled;

        /// <summary>
        /// Issue #20 acceptance: on first run, detect the NVIDIA driver and
        /// preselect the CUDA runtime when viable; CPU stays the silent default.
        /// The decision is persisted so the prompt only ever appears once.
        /// </summary>
        private async void MaybeFirstRunGpuPrompt()
        {
            if (_settings.WhisperRuntime != null || _gpuPromptHandled) return;
            _gpuPromptHandled = true;
            var gpu = await Task.Run(() => GpuDetector.DetectNvidia());
            if (_settings.WhisperRuntime != null) return; // user decided meanwhile elsewhere
            if (gpu == null)
            {
                _settings.WhisperRuntime = "cpu";
                _settings.Save();
                return;
            }
            var answer = MessageBox.Show(this,
                $"NVIDIA GPU detected:\n{gpu.Description}\n\n" +
                "Enable faster GPU transcription?\n\n" +
                "One-time ~330 MB download of the Whisper CUDA runtime into\n" +
                "%LOCALAPPDATA%\\BiscottiWin\\runtimes.",
                "Faster transcription", MessageBoxButton.YesNo, MessageBoxImage.Question);
            var yes = answer == MessageBoxResult.Yes;
            _settings.WhisperRuntime = yes ? "gpu" : "cpu";
            _settings.Save();
            if (yes) await EnableGpuRuntimeAsync();
        }

        private async Task EnableGpuRuntimeAsync()
        {
            if (new GpuRuntimeStore().IsInstalled())
            {
                WhisperRuntimeConfigurator.Apply(_settings);
                return;
            }
            ModelProgress.Visibility = Visibility.Visible;
            ModelProgress.Value = 0;
            RecordButton.IsEnabled = false;
            try
            {
                var progress = new Progress<(double pct, string phase)>(p =>
                {
                    ModelProgress.Value = p.pct;
                    StatusText.Text = p.phase;
                });
                await new GpuRuntimeStore().InstallAsync(progress);
                WhisperRuntimeConfigurator.Apply(_settings);
                StatusText.Text = "GPU transcription enabled.";
            }
            catch (Exception ex)
            {
                // any GPU failure keeps CPU transcription intact (issue #3 lesson)
                MessageBox.Show(this, ex.Message, "GPU runtime setup failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                _settings.WhisperRuntime = "cpu";
                _settings.Save();
                WhisperRuntimeConfigurator.Apply(_settings);
                StatusText.Text = "Staying on CPU transcription.";
            }
            finally
            {
                ModelProgress.Visibility = Visibility.Collapsed;
                UpdateModelButtons();
            }
        }

        private DateTime _recordingStartedUtc;

        /// <summary>
        /// Live proof of recording (issue #21): elapsed time plus per-channel
        /// bytes, with a warning when a channel looks dead so "is it working?"
        /// never requires opening files afterwards.
        /// </summary>
        private void UpdateRecordingHealth()
        {
            if (!_recording) return;
            var (mic, sys) = _meetings.RecordingStats;
            var elapsed = DateTime.UtcNow - _recordingStartedUtc;
            var micMb = (mic / 1024.0 / 1024.0).ToString("0.0");
            var sysMb = (sys / 1024.0 / 1024.0).ToString("0.0");
            var warning = "";
            if (mic == 0 && sys == 0) warning = "  ⚠ no audio captured on either channel — check microphone in Windows sound settings";
            else if (mic == 0) warning = "  ⚠ mic silent";
            else if (sys == 0) warning = "  ⚠ system audio not detected — is anything playing?";
            StatusText.Text = $"Recording… {elapsed:hh\\:mm\\:ss} · mic {micMb} MB · speaker {sysMb} MB{warning} · {_meetings.SystemAudioStatus}";
        }

        private ModelStore.WhisperModel CurrentOrPreferredModel() =>
            ModelStore.All.FirstOrDefault(m => m.Id == _settings.WhisperModelId) ?? ModelStore.All[0];

        /// <summary>
        /// The single place that keeps the model-driven UI state in sync:
        /// Record runs only with the model on disk, and the Download button
        /// is visible only while the model is missing or incomplete (issue
        /// #35) — a complete install never shows an action that reads as a
        /// required repeat download.
        /// </summary>
        private void UpdateModelButtons()
        {
            var downloaded = ModelStore.IsDownloaded(CurrentOrPreferredModel());
            RecordButton.IsEnabled = downloaded;
            ModelButton.Visibility = downloaded ? Visibility.Collapsed : Visibility.Visible;
        }

        private void RefreshList(Meeting? update = null)
        {
            var meetings = MeetingStore.LoadAll();
            var selectedId = (MeetingList.SelectedItem as Meeting)?.Id;
            MeetingList.ItemsSource = meetings;
            if (update != null)
                MeetingList.SelectedItem = meetings.FirstOrDefault(m => m.Id == update.Id) ?? meetings.FirstOrDefault();
            else if (selectedId != Guid.Empty)
                MeetingList.SelectedItem = meetings.FirstOrDefault(m => m.Id == selectedId);
        }

        private async void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_recording)
            {
                try
                {
                    var selectedApp = AudioAppPicker.SelectedItem as AudioAppChoice;
                    await _meetings.StartRecordingAsync(selectedApp?.ProcessId, selectedApp?.ProcessName);
                    _recording = true;
                    AudioAppPicker.IsEnabled = false;
                    _recordingStartedUtc = DateTime.UtcNow;
                    if (_meetings.Current is { } meeting)
                    {
                        _liveMeetingWindow = new MeetingWindow(meeting, isLiveRecording: true) { Owner = this };
                        _liveMeetingWindow.Closed += (_, _) => _liveMeetingWindow = null;
                        _liveMeetingWindow.Show();
                    }
                    RecordButton.Content = "■  Stop";
                    RecordButton.Background = new SolidColorBrush(Color.FromRgb(0x27, 0x7D, 0x5E));
                    _healthTimer.Start();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Could not start recording");
                }
                return;
            }

            _recording = false;
            AudioAppPicker.IsEnabled = true;
            _healthTimer.Stop();
            RecordButton.Content = "●  Start recording";
            RecordButton.Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
            RecordButton.IsEnabled = false;
            try
            {
                await _meetings.StopAndProcessAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Processing failed");
            }
            finally
            {
                _liveMeetingWindow?.SetRecordingComplete();
                UpdateModelButtons();
            }
        }

        private async void ModelButton_Click(object sender, RoutedEventArgs e)
        {
            var model = CurrentOrPreferredModel();
            ModelProgress.Visibility = Visibility.Visible;
            ModelProgress.Value = 0;
            RecordButton.IsEnabled = false;
            try
            {
                var progress = new Progress<(double pct, string phase)>(p =>
                {
                    ModelProgress.Value = p.pct;
                    StatusText.Text = p.phase;
                });
                await ModelStore.EnsureDownloadedAsync(model, progress);
                StatusText.Text = $"{model.DisplayName} ready.";
                RefreshList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Model download failed");
            }
            finally
            {
                ModelProgress.Visibility = Visibility.Collapsed;
                UpdateModelButtons();
            }
        }

        private void RefreshAudioApps_Click(object sender, RoutedEventArgs e) => RefreshAudioApps();

        private void RefreshAudioApps()
        {
            var selectedPid = (AudioAppPicker.SelectedItem as AudioAppChoice)?.ProcessId;
            var choices = new List<AudioAppChoice> { new(null, null, "All speaker audio (device loopback)") };
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == Environment.ProcessId || string.IsNullOrWhiteSpace(process.MainWindowTitle))
                            continue;
                        var processName = process.ProcessName;
                        choices.Add(new AudioAppChoice(process.Id, processName,
                            $"{process.MainWindowTitle} — {processName} (PID {process.Id})"));
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }

            AudioAppPicker.ItemsSource = choices.OrderBy(c => c.ProcessId.HasValue ? 1 : 0)
                .ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
            AudioAppPicker.SelectedItem = choices.FirstOrDefault(c => c.ProcessId == selectedPid) ?? choices[0];
        }

        private void MeetingList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // single-click just previews status; double-click opens for editing
        }

        private void MeetingList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (MeetingList.SelectedItem is Meeting m)
                new MeetingWindow(m).ShowDialog();
            RefreshList();
        }
    }

    internal sealed record AudioAppChoice(int? ProcessId, string? ProcessName, string DisplayName);
}
