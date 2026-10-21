using System.Globalization;
using System.Windows.Data;
using Biscotti.Core;
using Biscotti.Core.Models;

namespace Biscotti.Win;

public sealed class LocalStartTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime startedUtc) return string.Empty;
        return $"{MeetingTimeDisplay.ToLocalStartTime(startedUtc):yyyy-MM-dd HH:mm} (local time)";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class MeetingDurationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Meeting meeting) return string.Empty;
        return $"Duration {MeetingTimeDisplay.FormatDuration(MeetingTimeDisplay.Duration(meeting))}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
