using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;

namespace Biscotti.Win
{
    public partial class MeetingWindow : Window
    {
        private readonly Meeting _meeting;

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
