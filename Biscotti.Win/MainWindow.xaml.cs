using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        private readonly System.Windows.Threading.DispatcherTimer _healthTimer;
        private DateTime _recordingStartedUtc;
        private DateTime _micLastProgressUtc;
        private DateTime _sysLastProgressUtc;
        private long _micSeenBytes;
        private long _sysSeenBytes;

        public MainWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Load();
            _meetings = new MeetingService(_settings);
            _meetings.StateChanged += (state, msg) => Dispatcher.Invoke(() => StatusText.Text = msg);
            _meetings.CurrentMeetingUpdated += m => Dispatcher.Invoke(() => RefreshList(m));
            RefreshList();
            if (!ModelStore.IsDownloaded(CurrentOrPreferredModel()))
                RecordButton.IsEnabled = false;

            _healthTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _healthTimer.Tick += (_, _) => UpdateRecordingHealth();
        }

        /// <summary>
        /// Live proof of recording (issue #21): elapsed time, bytes per channel,
        /// and a warning when a channel has been silent for 30 s. "Is it
        /// working?" must not require opening the WAVs after the meeting.
        /// </summary>
        private void UpdateRecordingHealth()
        {
            if (!_recording) return;

            var now = DateTime.UtcNow;
            var (mic, sys) = _meetings.RecordingStats;
            if (mic != _micSeenBytes) { _micSeenBytes = mic; _micLastProgressUtc = now; }
            if (sys != _sysSeenBytes) { _sysSeenBytes = sys; _sysLastProgressUtc = now; }

            var elapsed = now - _recordingStartedUtc;
            StatusText.Text =
                $"Recording… {elapsed:hh\\:mm\\:ss} · mic {FormatBytes(mic)} · speaker {FormatBytes(sys)}"
                + SilenceWarning(now, elapsed, mic, sys);
        }

        private string SilenceWarning(DateTime now, TimeSpan elapsed, long mic, long sys)
        {
            if (_meetings.RecordingError is not null)
                return "  ⚠ mic unavailable — check Windows sound settings";

            // Let both devices open before claiming nothing is arriving.
            if (elapsed < TimeSpan.FromSeconds(3)) return "";
            if (mic == 0 && sys == 0)
                return "  ⚠ no audio captured on either channel — check Windows sound settings";

            var stalled = TimeSpan.FromSeconds(30);
            var micStalled = mic == 0 ? elapsed > stalled : now - _micLastProgressUtc > stalled;
            var sysStalled = sys == 0 ? elapsed > stalled : now - _sysLastProgressUtc > stalled;

            if (micStalled && !sysStalled) return "  ⚠ no mic audio for 30 s";
            if (sysStalled && !micStalled) return "  ⚠ system audio not detected — is anything playing?";
            return "";
        }

        private void StartHealthReadout()
        {
            var now = DateTime.UtcNow;
            _recordingStartedUtc = now;
            _micLastProgressUtc = now;
            _sysLastProgressUtc = now;
            _micSeenBytes = 0;
            _sysSeenBytes = 0;
            UpdateRecordingHealth();
            _healthTimer.Start();
        }

        private static string FormatBytes(long bytes) => bytes >= 1_048_576
            ? $"{bytes / 1_048_576.0:0.0} MB"
            : $"{bytes / 1024.0:0.0} KB";

        private ModelStore.WhisperModel CurrentOrPreferredModel() =>
            ModelStore.All.FirstOrDefault(m => m.Id == _settings.WhisperModelId) ?? ModelStore.All[0];

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
                    _meetings.StartRecording();
                    _recording = true;
                    RecordButton.Content = "■  Stop";
                    RecordButton.Background = new SolidColorBrush(Color.FromRgb(0x27, 0x7D, 0x5E));
                    StartHealthReadout();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Could not start recording");
                }
                return;
            }

            _recording = false;
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
                RecordButton.IsEnabled = true;
                if (!ModelStore.IsDownloaded(CurrentOrPreferredModel()))
                    RecordButton.IsEnabled = false;
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
                RecordButton.IsEnabled = true;
            }
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
}
