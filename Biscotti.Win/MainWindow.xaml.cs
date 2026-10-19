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
            StatusText.Text = $"Recording… {elapsed:hh\\:mm\\:ss} · mic {micMb} MB · speaker {sysMb} MB{warning}";
        }

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
                    _recordingStartedUtc = DateTime.UtcNow;
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
