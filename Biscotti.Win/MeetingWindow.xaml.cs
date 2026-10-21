using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using Biscotti.Core.Audio;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;
using NAudio.Wave;

namespace Biscotti.Win
{
    public partial class MeetingWindow : Window
    {
        private readonly Meeting _meeting;
        private readonly bool _designPreview;
        private bool _isLiveRecording;
        private bool _isProcessing;
        private WaveOutEvent? _audioOutput;
        private WaveFileReader? _audioReader;
        private EventHandler<StoppedEventArgs>? _playbackStoppedHandler;
        private string? _playingChannel;
        private readonly Action<string, double>? _addTimestampedNote;
        private readonly Func<double>? _recordingElapsedSeconds;

        public MeetingWindow(Meeting meeting, bool isLiveRecording = false,
            Action<string, double>? addTimestampedNote = null, Func<double>? recordingElapsedSeconds = null,
            bool designPreview = false)
        {
            InitializeComponent();
            _meeting = meeting;
            _designPreview = designPreview;
            _isLiveRecording = isLiveRecording;
            _addTimestampedNote = addTimestampedNote;
            _recordingElapsedSeconds = recordingElapsedSeconds;
            DataContext = meeting;
            LiveStatus.Visibility = isLiveRecording ? Visibility.Visible : Visibility.Collapsed;
            LiveNoteEditor.Visibility = isLiveRecording && addTimestampedNote is not null && recordingElapsedSeconds is not null
                ? Visibility.Visible : Visibility.Collapsed;
            DeleteButton.IsEnabled = !isLiveRecording;
            SaveButton.IsEnabled = !isLiveRecording;
            TitleBox.IsReadOnly = isLiveRecording;
            SummaryBox.IsReadOnly = isLiveRecording;
            NotesBox.IsReadOnly = isLiveRecording;
            NotesBox.Visibility = isLiveRecording ? Visibility.Collapsed : Visibility.Visible;
            NotesGrid.RowDefinitions[2].Height = isLiveRecording ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            AudioTab.IsEnabled = !isLiveRecording;
            TransferControls.IsEnabled = !isLiveRecording;
            RefreshTimestampedNotes();
            UpdateReviewState();
            if (!isLiveRecording && !string.IsNullOrWhiteSpace(meeting.Summary)) ReviewTabs.SelectedItem = SummaryTab;
            Closed += (_, _) => StopPlayback();
        }

        public void RefreshTranscript()
        {
            TranscriptList.ItemsSource = null;
            TranscriptList.ItemsSource = _meeting.Transcript;
            TranscriptScroller.ScrollToEnd();
            RefreshTimestampedNotes();
            UpdateReviewState();
        }

        private void UpdateReviewState()
        {
            TranscriptEmptyText.Visibility = _meeting.Transcript.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TranscriptEmptyText.Text = _isLiveRecording ? "Listening for speech… Your live transcript will appear here."
                : "No transcript is available for this meeting yet.";
            SummaryEmptyText.Visibility = string.IsNullOrWhiteSpace(_meeting.Summary) ? Visibility.Visible : Visibility.Collapsed;
            SummaryEmptyText.Text = _isProcessing ? "Preparing your summary locally…"
                : _isLiveRecording ? "Your summary will be prepared locally after recording stops."
                : "No summary yet. Summaries need Ollama running locally with your configured model.";
            ActionItemsGroup.Visibility = _meeting.ActionItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void RefreshTimestampedNotes()
        {
            var notes = _meeting.TimestampedNotes.OrderBy(note => note.StartSeconds).ToList();
            TimestampedNotesList.ItemsSource = notes;
            TimestampedNotesScroller.Visibility = notes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            NotesGrid.RowDefinitions[1].Height = notes.Count == 0 ? new GridLength(0) : GridLength.Auto;
        }

        private void AddLiveNote_Click(object sender, RoutedEventArgs e)
        {
            var text = LiveNoteInput.Text.Trim();
            if (text.Length == 0) return;

            try
            {
                _addTimestampedNote?.Invoke(text, _recordingElapsedSeconds?.Invoke() ?? 0);
                LiveNoteInput.Clear();
                LiveNoteStatus.Text = "Note saved with the current recording time.";
            }
            catch (Exception ex)
            {
                LiveNoteStatus.Text = $"Could not save note. {ex.Message}";
            }
        }

        public void StopAddingTimestampedNotes()
        {
            _isProcessing = true;
            LiveStatus.Text = "Processing · preparing your transcript and summary locally";
            SummaryEmptyText.Text = "Preparing your summary locally…";
            LiveNoteInput.IsEnabled = false;
            AddLiveNoteButton.IsEnabled = false;
            LiveNoteStatus.Text = "Recording stopped; notes are read-only while the meeting finishes.";
        }

        public void SetRecordingComplete()
        {
            _isLiveRecording = false;
            _isProcessing = false;
            LiveStatus.Visibility = Visibility.Collapsed;
            DeleteButton.IsEnabled = true;
            SaveButton.IsEnabled = true;
            TitleBox.IsReadOnly = false;
            SummaryBox.IsReadOnly = false;
            NotesBox.IsReadOnly = false;
            LiveNoteEditor.Visibility = Visibility.Collapsed;
            NotesBox.Visibility = Visibility.Visible;
            NotesGrid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
            AudioTab.IsEnabled = true;
            TransferControls.IsEnabled = true;
            TitleBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
            SummaryBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
            GetBindingExpression(TitleProperty)?.UpdateTarget();
            ActionItemsList.ItemsSource = _meeting.ActionItems;
            RefreshTranscript();
            if (!string.IsNullOrWhiteSpace(_meeting.Summary)) ReviewTabs.SelectedItem = SummaryTab;
        }

        private void MicPlay_Click(object sender, RoutedEventArgs e) =>
            BeginPlayback("microphone", Path.Combine(MeetingStore.MeetingDir(_meeting.Id), "mic.wav"));

        private void SysPlay_Click(object sender, RoutedEventArgs e) =>
            BeginPlayback("system", Path.Combine(MeetingStore.MeetingDir(_meeting.Id), "sys.wav"));

        private void MicStop_Click(object sender, RoutedEventArgs e) => StopPlaybackFor("microphone");

        private void SysStop_Click(object sender, RoutedEventArgs e) => StopPlaybackFor("system");

        private void BeginPlayback(string channel, string path)
        {
            if (_designPreview) { AudioStatus.Text = "Preview only. No meeting audio is opened."; return; }
            var stopError = StopPlayback();
            if (stopError is not null)
            {
                AudioStatus.Text = $"Could not stop the previous audio playback. {stopError.Message}";
                return;
            }
            Stream? stream = null;
            WaveFileReader? reader = null;
            WaveOutEvent? output = null;
            try
            {
                stream = EncryptedAudioPlayback.OpenWavStream(path);
                reader = new WaveFileReader(stream);
                stream = null; // WaveFileReader owns the stream.
                if (reader.Length <= 44) throw new InvalidDataException($"The {channel} recording contains no audio.");

                output = new WaveOutEvent();
                EventHandler<StoppedEventArgs>? stoppedHandler = null;
                stoppedHandler = (_, args) =>
                {
                    if (!Dispatcher.HasShutdownStarted)
                        Dispatcher.BeginInvoke(new Action(() => PlaybackStopped(output, reader, channel, stoppedHandler!, args)));
                };

                _audioReader = reader;
                _audioOutput = output;
                _playbackStoppedHandler = stoppedHandler;
                _playingChannel = channel;
                output.PlaybackStopped += stoppedHandler;
                output.Init(reader);
                output.Play();
                UpdatePlaybackButtons();
                AudioStatus.Text = $"Playing {channel} audio…";
            }
            catch (Exception ex)
            {
                stream?.Dispose();
                if (ReferenceEquals(_audioReader, reader)) StopPlayback();
                else
                {
                    output?.Dispose();
                    reader?.Dispose();
                }
                AudioStatus.Text = $"Could not play {channel} audio. {ex.Message}";
            }
        }

        private void PlaybackStopped(WaveOutEvent output, WaveFileReader reader, string channel,
            EventHandler<StoppedEventArgs> stoppedHandler, StoppedEventArgs args)
        {
            if (!ReferenceEquals(_audioOutput, output)) return;
            output.PlaybackStopped -= stoppedHandler;
            _audioOutput = null;
            _audioReader = null;
            _playbackStoppedHandler = null;
            _playingChannel = null;
            Exception? cleanupError = null;
            try { output.Dispose(); } catch (Exception ex) { cleanupError = ex; }
            try { reader.Dispose(); } catch (Exception ex) { cleanupError ??= ex; }
            UpdatePlaybackButtons();
            AudioStatus.Text = args.Exception is null
                ? cleanupError is null
                    ? $"{char.ToUpperInvariant(channel[0])}{channel[1..]} audio playback finished."
                    : $"Could not close {channel} audio playback cleanly. {cleanupError.Message}"
                : $"Could not play {channel} audio. {args.Exception.Message}";
        }

        private void StopPlaybackFor(string channel)
        {
            if (_playingChannel != channel) return;
            var error = StopPlayback();
            AudioStatus.Text = error is null
                ? $"{char.ToUpperInvariant(channel[0])}{channel[1..]} audio playback stopped."
                : $"Could not stop {channel} audio playback cleanly. {error.Message}";
        }

        private Exception? StopPlayback()
        {
            var output = _audioOutput;
            var reader = _audioReader;
            var handler = _playbackStoppedHandler;
            _audioOutput = null;
            _audioReader = null;
            _playbackStoppedHandler = null;
            _playingChannel = null;
            UpdatePlaybackButtons();
            Exception? error = null;
            if (output is not null)
            {
                if (handler is not null) output.PlaybackStopped -= handler;
                try { output.Stop(); } catch (Exception ex) { error = ex; }
                try { output.Dispose(); } catch (Exception ex) { error ??= ex; }
            }
            try { reader?.Dispose(); } catch (Exception ex) { error ??= ex; }
            return error;
        }

        private void UpdatePlaybackButtons()
        {
            if (MicStopButton is null || SysStopButton is null) return;
            MicStopButton.IsEnabled = _playingChannel == "microphone";
            SysStopButton.IsEnabled = _playingChannel == "system";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_designPreview) { Close(); return; }
            try
            {
                MeetingStore.Save(_meeting);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"The meeting could not be protected and saved. Keep this window open and retry after checking the Windows profile and storage permissions.\n\n{ex.Message}",
                    "Meeting not saved", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_designPreview) { Close(); return; }
            var ok = MessageBox.Show(this, "Delete this meeting and its audio? This cannot be undone.",
                "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.Yes) return;
            var stopError = StopPlayback();
            if (stopError is not null)
            {
                MessageBox.Show(this, $"Could not stop audio playback. The meeting was not deleted.\n\n{stopError.Message}",
                    "Could not delete meeting", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            MeetingStore.Delete(_meeting.Id);
            Close();
        }
    }

    public class SecondsToTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            TimeSpan.FromSeconds(System.Convert.ToDouble(value ?? 0.0)).ToString(@"hh\:mm\:ss");

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
