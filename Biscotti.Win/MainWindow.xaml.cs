using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using Biscotti.Core;
using Biscotti.Core.Audio;
using Biscotti.Core.Calendar;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;
using Biscotti.Core.Transcription;

namespace Biscotti.Win
{
    public partial class MainWindow : Window
    {
        private readonly MeetingService _meetings;
        private readonly AppSettings _settings;
        private readonly bool _designPreview;
        private CancellationTokenSource? _modelDownload;
        private bool _recording;
        private Stopwatch? _recordingStopwatch;
        private bool _meetingLoadErrorShown;
        private MeetingWindow? _liveMeetingWindow;
        private AudioDevicesWindow? _deviceDialog;
        private readonly System.Windows.Threading.DispatcherTimer _healthTimer;
        private readonly System.Windows.Threading.DispatcherTimer _calendarTimer = new() { Interval = TimeSpan.FromMinutes(1) };
        private IReadOnlyList<CalendarMeeting> _upcomingMeetings = Array.Empty<CalendarMeeting>();

        public MainWindow(bool designPreview = false)
        {
            InitializeComponent();
            _designPreview = designPreview;
            _settings = designPreview ? new AppSettings() : AppSettings.Load();
            if (!designPreview) WhisperRuntimeConfigurator.Apply(_settings); // before any WhisperFactory is created
            _meetings = new MeetingService(_settings);
            AutoStopBox.IsChecked = _settings.AutoStopEnabled;
            _meetings.StateChanged += (state, msg) => Dispatcher.Invoke(() =>
            {
                if (state == RecordingState.Transcribing && _recording)
                {
                    _recording = false;
                    _healthTimer?.Stop();
                    _liveMeetingWindow?.StopAddingTimestampedNotes();
                    RecordButton.Content = "●  Start recording";
                    RecordButton.SetResourceReference(BackgroundProperty, "AccentBrush");
                    RecordButton.IsEnabled = false;
                }
                StatusText.Text = msg;
                UpdateRecordingPresentation(state);
                UpdateAutoStopNotice(state == RecordingState.Recording ? _meetings.AutoStopNotice : null);
                UpdateDeviceControls();
                if (state == RecordingState.Idle)
                {
                    UpdateModelButtons();
                    if (!_recording) _liveMeetingWindow?.SetRecordingComplete();
                }
            });
            _meetings.AudioWarning += warning => Dispatcher.Invoke(() =>
                MessageBox.Show(this, warning, "Audio recovery", MessageBoxButton.OK, MessageBoxImage.Warning));
            _meetings.CurrentMeetingUpdated += m => Dispatcher.Invoke(() =>
            {
                RefreshList(m);
                if (_liveMeetingWindow?.IsVisible == true && _liveMeetingWindow.DataContext is Meeting live && live.Id == m.Id)
                    _liveMeetingWindow.RefreshTranscript();
            });
            if (!designPreview) Loaded += OnFirstLoaded;
            var model = CurrentOrPreferredModel();
            if (!designPreview && !ModelStore.IsDownloaded(model) && System.IO.File.Exists(ModelStore.ModelPath(model)))
            {
                // issue #32: present-but-truncated file — Download is the repair path
                StatusText.Text = "Model file incomplete or corrupt — press Download model to repair.";
            }
            _healthTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _healthTimer.Tick += (_, _) => UpdateRecordingHealth();
            _calendarTimer.Tick += async (_, _) => await RefreshCalendarMeetingsAsync();
        }

        private async void OnFirstLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnFirstLoaded;
            RefreshAudioApps();
            UpdateModelButtons();
            MaybeFirstRunGpuPrompt();
            await RefreshSummaryReadinessAsync();
            await RefreshCalendarMeetingsAsync();
            _calendarTimer.Start();
            await LoadInitialMeetingsAsync();
        }

        private async Task LoadInitialMeetingsAsync(Guid? preferredId = null)
        {
            var version = ++_historyLoadVersion;
            var selectedId = preferredId ?? SelectedHistoryMeeting?.Id;
            try
            {
                // Legacy audio migration can read and verify large WAV files. Keep it
                // off the startup path so the main window and tray appear immediately.
                var meetings = await MeetingStore.LoadAllAsync();
                if (version != _historyLoadVersion) return;
                SetHistory(meetings, selectedId);
            }
            catch (Exception ex)
            {
                if (version != _historyLoadVersion) return;
                ShowHistoryFailure();
                HistoryStatus.Visibility = Visibility.Visible;
                StatusText.Text = "Protected meeting data or audio could not be opened or migrated.";
                if (!_meetingLoadErrorShown)
                {
                    _meetingLoadErrorShown = true;
                    MessageBox.Show(this,
                        $"A meeting's protected data or audio could not be opened or migrated. Loading stopped rather than silently omitting that data.\n\n{ex.Message}\n\nProtected data belongs to the Windows profile that created it. After signing into that profile, close and reopen Biscotti to retry. An administrator password reset can make DPAPI data unavailable.",
                        "Meeting data unavailable", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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
            ModelButton.IsEnabled = false;
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

        private void UpdateDeviceControls()
        {
            var canEdit = _meetings.State == RecordingState.Idle;
            AudioDevicesButton.IsEnabled = canEdit;
            AudioAppPicker.IsEnabled = canEdit;
            _deviceDialog?.SetEditingEnabled(canEdit);
            UpdateCalendarControls();
        }

        private void UpdateCalendarControls()
        {
            var canEdit = _meetings.State == RecordingState.Idle;
            ChooseCalendarButton.IsEnabled = canEdit;
            RefreshCalendarButton.IsEnabled = canEdit;
            UpcomingMeetingsList.IsEnabled = canEdit;
        }

        private async Task RefreshCalendarMeetingsAsync(bool reportErrors = false)
        {
            var path = _settings.CalendarFilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                _upcomingMeetings = Array.Empty<CalendarMeeting>();
                UpcomingMeetingsList.ItemsSource = _upcomingMeetings;
                CalendarSourceText.Text = "";
                CalendarEmptyText.Text = "Choose a local .ics file to see events in the next 24 hours.";
                CalendarEmptyText.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                var meetings = await Task.Run(() => IcsCalendarReader.ReadUpcoming(path, DateTimeOffset.Now));
                _upcomingMeetings = meetings;
                UpcomingMeetingsList.ItemsSource = meetings;
                CalendarSourceText.Text = $"Local calendar: {System.IO.Path.GetFileName(path)} · refreshed {DateTime.Now:t}";
                CalendarEmptyText.Text = meetings.Count == 0
                    ? "No upcoming meetings in the next 24 hours."
                    : "";
                CalendarEmptyText.Visibility = meetings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                _upcomingMeetings = Array.Empty<CalendarMeeting>();
                UpcomingMeetingsList.ItemsSource = _upcomingMeetings;
                CalendarSourceText.Text = $"Local calendar: {System.IO.Path.GetFileName(path)}";
                CalendarEmptyText.Text = "The selected calendar file could not be read. Choose another .ics file or check that it is available.";
                CalendarEmptyText.Visibility = Visibility.Visible;
                if (reportErrors)
                    MessageBox.Show(this, ex.Message, "Calendar file unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void ChooseCalendarButton_Click(object sender, RoutedEventArgs e)
        {
            if (_designPreview) return;
            if (_meetings.State != RecordingState.Idle) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a local iCalendar export",
                Filter = "iCalendar files (*.ics)|*.ics|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(this) != true) return;

            var previous = _settings.CalendarFilePath;
            _settings.CalendarFilePath = dialog.FileName;
            try
            {
                _settings.Save();
                await RefreshCalendarMeetingsAsync(reportErrors: true);
            }
            catch (Exception ex)
            {
                _settings.CalendarFilePath = previous;
                MessageBox.Show(this, ex.Message, "Could not save calendar selection", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshCalendarButton_Click(object sender, RoutedEventArgs e) =>
            await (_designPreview ? Task.CompletedTask : RefreshCalendarMeetingsAsync(reportErrors: true));

        private void AudioDevicesButton_Click(object sender, RoutedEventArgs e) => ShowAudioDevices();

        private void ShowAudioDevices()
        {
            if (_designPreview)
            {
                new AudioDevicesWindow(_settings, () => true, designPreview: true) { Owner = this }.ShowDialog();
                return;
            }
            if (_meetings.State != RecordingState.Idle) return;
            _deviceDialog = new AudioDevicesWindow(_settings, () => _meetings.State == RecordingState.Idle) { Owner = this };
            try
            {
                if (_deviceDialog.ShowDialog() == true)
                    StatusText.Text = "Audio devices saved. Press Start recording when ready.";
            }
            finally { _deviceDialog = null; }
        }

        /// <summary>
        /// Live proof of recording (issue #21): elapsed time plus per-channel
        /// bytes, with a warning when a channel looks dead so "is it working?"
        /// never requires opening files afterwards.
        /// </summary>
        private void UpdateRecordingHealth()
        {
            if (!_recording) return;
            var (mic, sys) = _meetings.RecordingStats;
            var elapsed = _recordingStopwatch?.Elapsed ?? TimeSpan.Zero;
            RecordingDurationText.Text = $"Recording duration  {MeetingTimeDisplay.FormatDuration(elapsed)}";
            var micMb = (mic / 1024.0 / 1024.0).ToString("0.0");
            var sysMb = (sys / 1024.0 / 1024.0).ToString("0.0");
            var warning = "";
            if (mic == 0 && sys == 0) warning = "  ⚠ no audio captured on either channel — check microphone in Windows sound settings";
            else if (mic == 0) warning = "  ⚠ mic silent";
            else if (sys == 0) warning = "  ⚠ system audio not detected — is anything playing?";
            StatusText.Text = $"Recording health · mic {micMb} MB · speaker {sysMb} MB{warning} · {_meetings.SystemAudioStatus} · {_meetings.AutoStopStatus}";
            UpdateAutoStopNotice(_meetings.AutoStopNotice);
            DeviceWarningText.Text = _meetings.RecordingDeviceWarning;
            DeviceWarningText.Visibility = string.IsNullOrEmpty(DeviceWarningText.Text) ? Visibility.Collapsed : Visibility.Visible;
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
            if (_designPreview) return;
            var downloaded = ModelStore.IsDownloaded(CurrentOrPreferredModel());
            RecordButton.IsEnabled = _recording || downloaded && _meetings.State == RecordingState.Idle;
            ModelButton.Visibility = downloaded ? Visibility.Collapsed : Visibility.Visible;
            ModelButton.IsEnabled = _meetings.State == RecordingState.Idle && ModelProgress.Visibility != Visibility.Visible;
            UpdateRecordingPresentation(_meetings.State, downloaded);
        }

        private void UpdateRecordingPresentation(RecordingState state, bool? modelReady = null)
        {
            UpdateTransferControls(state);
            var recording = state == RecordingState.Recording;
            var busy = state != RecordingState.Idle && !recording;
            ProcessingProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            RecordingHeading.Text = recording ? "RECORDING NOW" : busy ? "PROCESSING" : "RECORD A MEETING";
            SetupText.Text = recording ? "Your meeting is being captured" : busy ? "Finishing your meeting"
                : modelReady == true ? "Ready when you are" : "Start with a local transcription model";
            SetupHint.Text = recording ? "Open the live meeting window for your transcript and timestamped notes. Stop when you're ready."
                : busy ? "Keep Biscotti open. Your transcript and summary are being prepared locally."
                : "Download Whisper once, then record and transcribe offline. Summaries use Ollama on this PC.";
            RecordButton.SetResourceReference(BackgroundProperty, recording ? "DangerBrush" : "AccentBrush");
            RecordButton.SetResourceReference(BorderBrushProperty, recording ? "DangerBrush" : "AccentBrush");
        }

        private async void RefreshList(Meeting? update = null) =>
            await LoadInitialMeetingsAsync(update?.Id);

        private async void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_designPreview) { TogglePreviewRecording(); return; }
            if (!_recording)
            {
                await StartRecordingAsync();
                return;
            }

            await StopRecordingAsync();
        }

        private async void RecordUpcomingMeeting_Click(object sender, RoutedEventArgs e)
        {
            if (_designPreview) { TogglePreviewRecording(); return; }
            if (sender is not Button { Tag: CalendarMeeting selectedMeeting }) return;
            await StartRecordingAsync(selectedMeeting);
        }

        private async Task StartRecordingAsync(CalendarMeeting? calendarMeeting = null, bool allowSystemWideCapture = false)
        {
            if (_meetings.State != RecordingState.Idle || _recording) return;
            try
            {
                RecordButton.IsEnabled = false;
                DeviceWarningText.Visibility = Visibility.Collapsed;
                if (calendarMeeting is not null)
                {
                    await RefreshCalendarMeetingsAsync();
                    calendarMeeting = _upcomingMeetings.FirstOrDefault(m => m.EventId == calendarMeeting.EventId);
                    if (calendarMeeting is null)
                    {
                        StatusText.Text = "That calendar event is no longer upcoming. Refresh the calendar list and try again.";
                        return;
                    }
                }
                var selectedApp = AudioAppPicker.SelectedItem as AudioAppChoice;
                await _meetings.StartRecordingAsync(selectedApp?.ProcessId, selectedApp?.ProcessName, calendarMeeting,
                    selectedApp?.ProcessId is null || allowSystemWideCapture);
                _recording = true;
                AudioAppPicker.IsEnabled = false;
                _recordingStopwatch = Stopwatch.StartNew();
                RecordingDurationText.Visibility = Visibility.Visible;
                if (_meetings.Current is { } meeting)
                {
                    _liveMeetingWindow = new MeetingWindow(meeting, isLiveRecording: true,
                        addTimestampedNote: _meetings.AddTimestampedNote,
                        recordingElapsedSeconds: () => _recordingStopwatch?.Elapsed.TotalSeconds ?? 0) { Owner = this };
                    _liveMeetingWindow.Closed += (_, _) => _liveMeetingWindow = null;
                    _liveMeetingWindow.Show();
                }
                RecordButton.Content = "■  Stop & save";
                UpdateRecordingPresentation(RecordingState.Recording);
                _healthTimer.Start();
                UpdateRecordingHealth();
            }
            catch (AudioDeviceException ex)
            {
                MessageBox.Show(this, ex.Message, "Choose audio devices", MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowAudioDevices();
            }
            catch (SystemWideCaptureConsentException ex)
            {
                var answer = MessageBox.Show(this,
                    $"{ex.Message}\n\nThis captures speaker audio from every app, which may include notifications, music, or unrelated conversations. Retry with all speaker audio?",
                    "Confirm all speaker audio", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer == MessageBoxResult.Yes)
                    await StartRecordingAsync(calendarMeeting, allowSystemWideCapture: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message + "\n\nIf microphone access is denied, open Settings > Microphone access for Windows permission guidance, then check Audio devices.", "Could not start recording");
            }
            finally
            {
                UpdateDeviceControls();
                UpdateModelButtons();
            }
        }

        private async Task StopRecordingAsync()
        {

            _recording = false;
            AudioAppPicker.IsEnabled = true;
            _healthTimer.Stop();
            _recordingStopwatch?.Stop();
            _recordingStopwatch = null;
            _liveMeetingWindow?.StopAddingTimestampedNotes();
            RecordingDurationText.Visibility = Visibility.Collapsed;
            RecordButton.Content = "●  Start recording";
            RecordButton.SetResourceReference(BackgroundProperty, "AccentBrush");
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
                UpdateDeviceControls();
            }
        }

        private async void ModelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_designPreview) { StatusText.Text = "Preview only. No models are downloaded."; return; }
            if (_modelDownload is not null) return;
            using var cancellation = new CancellationTokenSource();
            _modelDownload = cancellation;
            CancelModelButton.Visibility = Visibility.Visible;
            var model = CurrentOrPreferredModel();
            ModelProgress.Visibility = Visibility.Visible;
            ModelProgress.Value = 0;
            RecordButton.IsEnabled = false;
            ModelButton.IsEnabled = false;
            try
            {
                var progress = new Progress<(double pct, string phase)>(p =>
                {
                    ModelProgress.Value = p.pct;
                    StatusText.Text = p.phase;
                });
                await ModelStore.EnsureDownloadedAsync(model, progress, cancellation.Token);
                StatusText.Text = $"{model.DisplayName} ready.";
                RefreshList();
            }
            catch (OperationCanceledException) { StatusText.Text = "Download cancelled. Press Download Whisper model to retry."; }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Model download failed");
            }
            finally
            {
                _modelDownload = null;
                CancelModelButton.Visibility = Visibility.Collapsed;
                ModelProgress.Visibility = Visibility.Collapsed;
                UpdateModelButtons();
            }
        }

        private void CancelModel_Click(object sender, RoutedEventArgs e) => _modelDownload?.Cancel();

        private async Task RefreshSummaryReadinessAsync()
        {
            if (_designPreview) return;
            SummaryReadinessText.Text = "Checking local summary model…";
            var readiness = await Biscotti.Core.Ai.OllamaModels.CheckAsync();
            SummaryReadinessText.Text = readiness.Describe(_settings.OllamaModel);
        }

        private void RefreshAudioApps_Click(object sender, RoutedEventArgs e) => RefreshAudioApps();

        private void RefreshAudioApps()
        {
            if (_designPreview) return;
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
            OpenMeetingButton.IsEnabled = SelectedHistoryMeeting is Meeting;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_designPreview)
                new SettingsWindow(new AppSettings { LocalVocabulary = "Atlas, Biscotti" }, _ => null, designPreview: true) { Owner = this }.ShowDialog();
            else OpenSettingsFromShell();
        }

        private void OpenMeeting_Click(object sender, RoutedEventArgs e) => OpenSelectedMeeting();

        private void MeetingList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter || SelectedHistoryMeeting is not Meeting) return;
            e.Handled = true;
            OpenSelectedMeeting();
        }

        private void OpenSelectedMeeting()
        {
            if (SelectedHistoryMeeting is not Meeting meeting) return;
            var window = new MeetingWindow(meeting, designPreview: _designPreview,
                recoverInterruptedMeeting: _designPreview ? PreviewRecoverAsync : _meetings.RecoverInterruptedMeetingAsync,
                retryTranscription: _designPreview ? PreviewRetryTranscriptionAsync : _meetings.RetrySavedTranscriptionAsync,
                regenerateSummary: _designPreview ? PreviewRegenerateSummaryAsync : _meetings.RegenerateSavedSummaryAsync)
            { Owner = this };
            window.ShowDialog();
            if (!_designPreview) RefreshList();
        }

        private static Task PreviewRecoverAsync(Meeting meeting, CancellationToken ct)
        {
            meeting.EndedUtc ??= DateTime.UtcNow;
            meeting.ProcessingStatus = MeetingProcessingStatus.TranscriptionPending;
            return Task.CompletedTask;
        }

        private static Task PreviewRetryTranscriptionAsync(Meeting meeting, CancellationToken ct)
        {
            meeting.ProcessingStatus = MeetingProcessingStatus.SummaryPending;
            return Task.CompletedTask;
        }

        private static Task PreviewRegenerateSummaryAsync(Meeting meeting, CancellationToken ct)
        {
            meeting.ProcessingStatus = MeetingProcessingStatus.Complete;
            return Task.CompletedTask;
        }

        private void MeetingList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OpenSelectedMeeting();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_designPreview) { base.OnClosing(e); return; }
            if (!App.IsExitRequested)
            {
                e.Cancel = true;
                if (_meetings.State == RecordingState.Starting)
                    StatusText.Text = "Finishing the audio device check. The app will stay in the tray.";
                Hide();
                base.OnClosing(e);
                return;
            }
            if (_meetings.State != RecordingState.Idle)
            {
                e.Cancel = true;
                StatusText.Text = "Stop and finish processing the meeting before exiting.";
            }
            base.OnClosing(e);
        }

        internal async Task DisposeForShutdownAsync()
        {
            _healthTimer.Stop();
            _calendarTimer.Stop();
            _historySearchCancellation?.Cancel();
            _modelDownload?.Cancel();
            _transferCancellation?.Cancel();
            DisposeShell();
            await _meetings.DisposeAsync();
        }
    }
}

internal sealed record AudioAppChoice(int? ProcessId, string? ProcessName, string DisplayName)
{
    public override string ToString() => DisplayName;
}
