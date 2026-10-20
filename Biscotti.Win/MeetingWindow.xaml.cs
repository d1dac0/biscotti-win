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
        private WaveOutEvent? _audioOutput;
        private WaveFileReader? _audioReader;
        private EventHandler<StoppedEventArgs>? _playbackStoppedHandler;
        private string? _playingChannel;

        public MeetingWindow(Meeting meeting, bool isLiveRecording = false)
        {
            InitializeComponent();
            _meeting = meeting;
            DataContext = meeting;
            LiveStatus.Visibility = isLiveRecording ? Visibility.Visible : Visibility.Collapsed;
            DeleteButton.IsEnabled = !isLiveRecording;
            SaveButton.IsEnabled = !isLiveRecording;
            TitleBox.IsReadOnly = isLiveRecording;
            SummaryBox.IsReadOnly = isLiveRecording;
            NotesBox.IsReadOnly = isLiveRecording;
            AudioTab.IsEnabled = !isLiveRecording;
            Closed += (_, _) => StopPlayback();
        }

        public void RefreshTranscript()
        {
            TranscriptList.ItemsSource = null;
            TranscriptList.ItemsSource = _meeting.Transcript;
            TranscriptScroller.ScrollToEnd();
        }

        public void SetRecordingComplete()
        {
            LiveStatus.Visibility = Visibility.Collapsed;
            DeleteButton.IsEnabled = true;
            SaveButton.IsEnabled = true;
            TitleBox.IsReadOnly = false;
            SummaryBox.IsReadOnly = false;
            NotesBox.IsReadOnly = false;
            AudioTab.IsEnabled = true;
        }

        private void MicPlay_Click(object sender, RoutedEventArgs e) =>
            BeginPlayback("microphone", Path.Combine(MeetingStore.MeetingDir(_meeting.Id), "mic.wav"));

        private void SysPlay_Click(object sender, RoutedEventArgs e) =>
            BeginPlayback("system", Path.Combine(MeetingStore.MeetingDir(_meeting.Id), "sys.wav"));

        private void MicStop_Click(object sender, RoutedEventArgs e) => StopPlaybackFor("microphone");

        private void SysStop_Click(object sender, RoutedEventArgs e) => StopPlaybackFor("system");

        private void BeginPlayback(string channel, string path)
        {
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
            MeetingStore.Save(_meeting);
            Close();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
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
