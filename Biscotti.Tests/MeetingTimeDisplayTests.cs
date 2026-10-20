using Biscotti.Core;
using Biscotti.Core.Models;

namespace Biscotti.Tests;

public sealed class MeetingTimeDisplayTests
{
    [Fact]
    public void ToLocalStartTime_ConvertsUtcUsingCurrentWindowsTimeZone()
    {
        var startedUtc = new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);

        var result = MeetingTimeDisplay.ToLocalStartTime(startedUtc);

        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(startedUtc, TimeZoneInfo.Local), result);
        Assert.Equal(DateTimeKind.Local, result.Kind);
    }

    [Fact]
    public void FormatDuration_DoesNotWrapAtTwentyFourHours()
    {
        var meeting = new Meeting
        {
            StartedUtc = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc),
            EndedUtc = new DateTime(2026, 10, 21, 12, 34, 56, DateTimeKind.Utc),
        };

        Assert.Equal("36:34:56", MeetingTimeDisplay.FormatDuration(MeetingTimeDisplay.Duration(meeting)));
    }
}
