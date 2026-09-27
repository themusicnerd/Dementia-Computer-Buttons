using DementiaComputerButtons.Core.Services;

namespace DementiaComputerButtons.Tests;

public sealed class DailyTimeRangeTests
{
    [Theory]
    [InlineData(false, "22:00", "07:00", 23, 0, false)]
    [InlineData(true, "22:00", "07:00", 23, 0, true)]
    [InlineData(true, "22:00", "07:00", 6, 59, true)]
    [InlineData(true, "22:00", "07:00", 12, 0, false)]
    [InlineData(true, "09:00", "17:00", 12, 0, true)]
    [InlineData(true, "09:00", "17:00", 17, 0, false)]
    [InlineData(true, "00:00", "00:00", 12, 0, true)]
    public void SupportsDisabledDaytimeOvernightAndAllDayRanges(
        bool enabled, string from, string until, int hour, int minute, bool expected)
    {
        Assert.Equal(expected, DailyTimeRange.Contains(enabled, from, until, new TimeOnly(hour, minute)));
    }
}
