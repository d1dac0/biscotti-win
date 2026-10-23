using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Biscotti.Core.Audio;
using Biscotti.Core.Calendar;
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
        private IWavePlayer? _audioOutput;
        private readonly Func<string, Stream> _audioStreamFactory;
        private readonly Func<IWavePlayer> _audioOutputFactory;
        private WaveFileReader? _audioReader;
        private Stream? _audioSourceStream;
        private EventHandler<StoppedEventArgs>? _playbackStoppedHandler;
        private string? _playingChannel;
        private readonly DispatcherTimer _playbackTimer;
        private bool _isScrubbing;
        private bool _updatingPosition;
        private readonly Action<string, double>? _addTimestampedNote;
        private readonly Func<double>? _recordingElapsedSeconds;
        private readonly Func<Meeting, CancellationToken, Task>? _recoverInterruptedMeeting;
        private readonly Func<Meeting, CancellationToken, Task>? _retryTranscription;
        private readonly Func<Meeting, CancellationToken, Task>? _regenerateSummary;

        public MeetingWindow(Meeting meeting, bool isLiveRecording = false,
            Action<string, double>? addTimestampedNote = null, Func<double>? recordingElapsedSeconds = null,
            bool designPreview = false,
            Func<Meeting, CancellationToken, Task>? recoverInterruptedMeeting = null,
            Func<Meeting, CancellationToken, Task>? retryTranscription = null,
            Func<Meeting, CancellationToken, Task>? regenerateSummary = null,
            Action<Meeting>? saveMeeting = null,
            Func<string, Stream>? audioStreamFactory = null,
            Func<IWavePlayer>? audioOutputFactory = null,
            Func<DateTimeOffset, Task<IReadOnlyList<CalendarMeeting>>>? calendarEventsNear = null,
            Action<CalendarMeeting?>? setCalendarEvent = null)
        {
            InitializeComponent();
            _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _playbackTimer.Tick += (_, _) => RefreshTransportPosition();
            _meeting = meeting;
            _calendarEventsNear = calendarEventsNear;
            _setCalendarEvent = setCalendarEvent;
            _audioStreamFactory = audioStreamFactory ?? EncryptedAudioPlayback.OpenWavStream;
            _audioOutputFactory = audioOutputFactory ?? (() => new WaveOutEvent());
            _designPreview = designPreview;
            _saveMeeting = saveMeeting ?? (designPreview ? _ => { } : MeetingStore.Save);
            _isLiveRecording = isLiveRecording;
            _addTimestampedNote = addTimestampedNote;
            _recordingElapsedSeconds = recordingElapsedSeconds;
            _recoverInterruptedMeeting = recoverInterruptedMeeting;
            _retryTranscription = retryTranscription;
            _regenerateSummary = regenerateSummary;
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
            UpdateSpeakerControls();
            InitializeTags();
            RefreshTags();
            RefreshTimestampedNotes();
            TimestampedNotesExpander.IsExpanded = isLiveRecording;
            UpdateReviewState();
            RefreshProcessingPanel();
            if (!isLiveRecording && !string.IsNullOrWhiteSpace(meeting.Summary)) ReviewTabs.SelectedItem = SummaryTab;
            Closed += (_, _) =>
            {
                _playbackTimer.Stop();
                StopPlayback();
            };
            InitializeEditing();
            RefreshCalendarContext();
        }

        public void RefreshTranscript()
        {
            TranscriptList.ItemsSource = null;
            TranscriptList.ItemsSource = _meeting.Transcript;
            TranscriptScroller.ScrollToEnd();
            RefreshTimestampedNotes();
            UpdateReviewState();
            RefreshProcessingPanel();
        }

        private void UpdateReviewState()
        {
            RefreshCalendarContext();
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
            var notes = (_isLiveRecording ? _meeting.TimestampedNotes :
                _noteDrafts ??= _meeting.TimestampedNotes.Select(note => new TimestampedMeetingNote
                    { StartSeconds = note.StartSeconds, Text = note.Text }).ToList())
                .OrderBy(note => note.StartSeconds).ToList();
            TimestampedNotesList.ItemsSource = notes;
            TimestampedNotesScroller.Visibility = notes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            TimestampedNotesExpander.Visibility = notes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            TimestampedNotesExpander.Header = $"Timestamped notes ({notes.Count}) · review or edit";
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
            RefreshCalendarContext();
            TagsPanel.IsEnabled = false;
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
            TagsPanel.IsEnabled = true;
            RefreshTags();
            UpdateSpeakerControls();
            TitleBox.Text = _meeting.Title;
            SummaryBox.Text = _meeting.Summary ?? "";
            NotesBox.Text = _meeting.Notes ?? "";
            GetBindingExpression(TitleProperty)?.UpdateTarget();
            ActionItemsList.ItemsSource = _meeting.ActionItems;
            RefreshTranscript();
            CaptureSavedEdits();
            if (!string.IsNullOrWhiteSpace(_meeting.Summary)) ReviewTabs.SelectedItem = SummaryTab;
        }

        private string SelectedChannel => (AudioChannelSelector.SelectedItem as ComboBoxItem)?.Tag as string ?? "microphone";

        private static string AudioPath(string channel, Guid meetingId) =>
            Path.Combine(MeetingStore.MeetingDir(meetingId), channel == "system" ? "sys.wav" : "mic.wav");

        private void AudioPlayPause_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_audioOutput is null)
                {
                    BeginPlayback(SelectedChannel, AudioPath(SelectedChannel, _meeting.Id), AudioPositionSlider.Value);
                    return;
                }

                if (_audioOutput.PlaybackState == PlaybackState.Playing)
                {
                    _audioOutput.Pause();
                    _playbackTimer.Stop();
                    AudioPlayPauseButton.Content = "Resume";
                    AudioStatus.Text = $"Paused {_playingChannel} audio.";
                    RefreshTransportPosition();
                }
                else
                {
                    _audioOutput.Play();
                    _playbackTimer.Start();
                    AudioPlayPauseButton.Content = "Pause";
                    AudioStatus.Text = $"Playing {_playingChannel} audio…";
                }
                UpdatePlaybackButtons();
            }
            catch (Exception ex) { AudioStatus.Text = $"Could not control audio playback. {ex.Message}"; }
        }

        private void AudioStop_Click(object sender, RoutedEventArgs e)
        {
            var channel = _playingChannel;
            var error = StopPlayback();
            AudioStatus.Text = error is null
                ? channel is null ? "Audio playback stopped." : $"{char.ToUpperInvariant(channel[0])}{channel[1..]} audio playback stopped."
                : $"Could not stop audio playback cleanly. {error.Message}";
        }

        private void AudioChannel_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_audioOutput is null || _playingChannel == SelectedChannel) return;
            var startSeconds = CurrentPositionSeconds();
            var shouldPlay = _audioOutput.PlaybackState == PlaybackState.Playing;
            BeginPlayback(SelectedChannel, AudioPath(SelectedChannel, _meeting.Id), startSeconds, shouldPlay);
        }

        private void BeginPlayback(string channel, string path, double startSeconds = 0, bool autoPlay = true)
        {
            if (_designPreview) { AudioStatus.Text = "Preview only. No meeting audio is opened."; return; }
            var stopError = StopPlayback(resetPosition: false);
            if (stopError is not null)
            {
                AudioStatus.Text = $"Could not stop the previous audio playback. {stopError.Message}";
                return;
            }
            Stream? stream = null;
            WaveFileReader? reader = null;
            IWavePlayer? output = null;
            try
            {
                stream = _audioStreamFactory(path);
                reader = new WaveFileReader(stream);
                _audioSourceStream = stream;
                stream = null;
                if (reader.Length <= 44) throw new InvalidDataException($"The {channel} recording contains no audio.");
                reader.Position = PositionForSeconds(startSeconds, reader.Length);

                output = _audioOutputFactory();
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
                if (autoPlay) output.Play();
                if (autoPlay) _playbackTimer.Start();
                UpdatePlaybackButtons();
                AudioPlayPauseButton.Content = autoPlay ? "Pause" : "Resume";
                AudioStatus.Text = autoPlay ? $"Playing {channel} audio…" : $"Ready to play {channel} audio.";
                RefreshTransportPosition();
            }
            catch (Exception ex)
            {
                stream?.Dispose();
                if (ReferenceEquals(_audioReader, reader)) StopPlayback();
                else
                {
                    output?.Dispose();
                    reader?.Dispose();
                    _audioSourceStream?.Dispose();
                    _audioSourceStream = null;
                }
                AudioStatus.Text = $"Could not play {channel} audio. {ex.Message}";
            }
        }

        private void PlaybackStopped(IWavePlayer output, WaveFileReader reader, string channel,
            EventHandler<StoppedEventArgs> stoppedHandler, StoppedEventArgs args)
        {
            if (!ReferenceEquals(_audioOutput, output)) return;
            output.PlaybackStopped -= stoppedHandler;
            _audioOutput = null;
            _audioReader = null;
            var sourceStream = _audioSourceStream;
            _audioSourceStream = null;
            _playbackStoppedHandler = null;
            _playingChannel = null;
            _playbackTimer.Stop();
            Exception? cleanupError = null;
            try { output.Dispose(); } catch (Exception ex) { cleanupError = ex; }
            try { reader.Dispose(); } catch (Exception ex) { cleanupError ??= ex; }
            try { sourceStream?.Dispose(); } catch (Exception ex) { cleanupError ??= ex; }
            UpdatePlaybackButtons();
            AudioPlayPauseButton.Content = "Play";
            AudioPositionSlider.Value = 0;
            UpdatePositionText(0, 0);
            AudioStatus.Text = args.Exception is null
                ? cleanupError is null
                    ? $"{char.ToUpperInvariant(channel[0])}{channel[1..]} audio playback finished."
                    : $"Could not close {channel} audio playback cleanly. {cleanupError.Message}"
                : $"Could not play {channel} audio. {args.Exception.Message}";
        }

        private Exception? StopPlayback(bool resetPosition = true)
        {
            var output = _audioOutput;
            var reader = _audioReader;
            var sourceStream = _audioSourceStream;
            var handler = _playbackStoppedHandler;
            _audioOutput = null;
            _audioReader = null;
            _audioSourceStream = null;
            _playbackStoppedHandler = null;
            _playingChannel = null;
            _playbackTimer.Stop();
            UpdatePlaybackButtons();
            AudioPlayPauseButton.Content = "Play";
            if (resetPosition)
            {
                AudioPositionSlider.Value = 0;
                UpdatePositionText(0, 0);
            }
            Exception? error = null;
            if (output is not null)
            {
                if (handler is not null) output.PlaybackStopped -= handler;
                try { output.Stop(); } catch (Exception ex) { error = ex; }
                try { output.Dispose(); } catch (Exception ex) { error ??= ex; }
            }
            try { reader?.Dispose(); } catch (Exception ex) { error ??= ex; }
            try { sourceStream?.Dispose(); } catch (Exception ex) { error ??= ex; }
            return error;
        }

        private void UpdatePlaybackButtons()
        {
            if (AudioStopButton is null) return;
            AudioStopButton.IsEnabled = _audioOutput is not null;
            AudioPlayPauseButton.IsEnabled = true;
        }

        internal static long PositionForSeconds(double seconds, long wavLength)
        {
            const int bytesPerSecond = 16_000 * 2;
            var pcmLength = Math.Max(0, wavLength - 44);
            var requested = double.IsFinite(seconds) ? seconds : 0;
            var bytes = Math.Clamp(requested * bytesPerSecond, 0, pcmLength);
            bytes -= bytes % 2; // 16-bit PCM block alignment.
            return 44 + (long)bytes;
        }

        private double CurrentPositionSeconds() => _audioReader is null ? AudioPositionSlider.Value
            : Math.Max(0, _audioReader.Position - 44) / 32_000d;

        private void RefreshTransportPosition()
        {
            if (_audioReader is null || _isScrubbing) return;
            var position = Math.Max(0, _audioReader.Position - 44);
            var duration = Math.Max(0, _audioReader.Length - 44);
            _updatingPosition = true;
            try
            {
                AudioPositionSlider.Maximum = Math.Max(1, duration / 32_000d);
                AudioPositionSlider.Value = Math.Clamp(position / 32_000d, 0, AudioPositionSlider.Maximum);
            }
            finally { _updatingPosition = false; }
            UpdatePositionText(position / 32_000d, duration / 32_000d);
        }

        private void UpdatePositionText(double position, double duration) =>
            AudioPositionText.Text = $"{FormatAudioTime(position)} / {FormatAudioTime(duration)}";

        private static string FormatAudioTime(double seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
        }

        private void AudioSlider_MouseDown(object sender, MouseButtonEventArgs e) => _isScrubbing = true;

        private void AudioSlider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isScrubbing) return;
            _isScrubbing = false;
            SeekPlayback(AudioPositionSlider.Value, resume: _audioOutput?.PlaybackState == PlaybackState.Playing);
        }

        private void SeekPlayback(double seconds, bool resume)
        {
            if (_audioReader is null || _audioOutput is null) return;
            var output = _audioOutput;
            var wasPlaying = output.PlaybackState == PlaybackState.Playing;
            try
            {
                if (wasPlaying) output.Pause();
                _audioReader.Position = PositionForSeconds(seconds, _audioReader.Length);
                if (resume) output.Play();
                RefreshTransportPosition();
            }
            catch (Exception ex) { AudioStatus.Text = $"Could not seek audio. {ex.Message}"; }
        }

        private void SeekSelectedChannel(double seconds, bool startPlayback = false)
        {
            var channel = SelectedChannel;
            if (_audioReader is not null && _playingChannel == channel)
            {
                SeekPlayback(seconds, resume: startPlayback || _audioOutput?.PlaybackState == PlaybackState.Playing);
                return;
            }
            BeginPlayback(channel, AudioPath(channel, _meeting.Id), seconds);
        }

        private void AudioPosition_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingPosition || _isScrubbing || _audioReader is null) return;
            SeekPlayback(AudioPositionSlider.Value, resume: _audioOutput?.PlaybackState == PlaybackState.Playing);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox || Keyboard.FocusedElement is Slider) return;
            if (e.Key == Key.Space)
            {
                AudioPlayPause_Click(AudioPlayPauseButton, new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
            else if (e.Key is Key.Left or Key.Right)
            {
                var delta = e.Key == Key.Left ? -5d : 5d;
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) delta *= 6;
                SeekSelectedChannel(Math.Max(0, CurrentPositionSeconds() + delta));
                e.Handled = true;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            TrySaveEdits();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_designPreview) { Close(); return; }
            var ok = MessageBox.Show(this, "Delete this meeting and its audio? This cannot be undone.",
                "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.Yes) return;
            if (!TryDeleteMeeting(() => MeetingStore.Delete(_meeting.Id), stopError =>
            {
                MessageBox.Show(this, $"Could not stop audio playback. The meeting was not deleted.\n\n{stopError.Message}",
                    "Could not delete meeting", MessageBoxButton.OK, MessageBoxImage.Error);
            })) return;
            _discardEditsOnClose = true;
            Close();
        }

        internal bool TryDeleteMeeting(Action deleteMeeting, Action<Exception>? reportStopError = null)
        {
            var stopError = StopPlayback();
            if (stopError is not null)
            {
                reportStopError?.Invoke(stopError);
                return false;
            }
            deleteMeeting();
            return true;
        }
    }

    public class SecondsToTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            TimeSpan.FromSeconds(System.Convert.ToDouble(value ?? 0.0)).ToString(@"hh\:mm\:ss");

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
