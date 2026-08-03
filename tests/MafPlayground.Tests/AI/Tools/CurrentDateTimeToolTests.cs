using MafPlayground.AI.Tools;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests;

public sealed class CurrentDateTimeToolTests
{
    [Fact]
    public void GetCurrentDateTime_ReturnsDateAndTimeInRequestedTimeZone()
    {
        TimeProvider timeProvider = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 3, 15, 42, 17, TimeSpan.Zero));
        CurrentDateTimeTool tool = new(timeProvider);

        CurrentDateTimeResult result = tool.GetCurrentDateTime(
            "America/Argentina/Buenos_Aires");

        Assert.Equal("2026-08-03", result.Date);
        Assert.Equal("12:42:17", result.Time);
        Assert.Equal("Monday", result.DayOfWeek);
        Assert.Equal("America/Argentina/Buenos_Aires", result.TimeZoneId);
        Assert.Equal("-03:00", result.UtcOffset);
    }

    [Fact]
    public void GetCurrentDateTime_RejectsUnknownTimeZone()
    {
        CurrentDateTimeTool tool = new(TimeProvider.System);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            tool.GetCurrentDateTime("Not/A_Time_Zone"));

        Assert.Contains("not available", exception.Message);
    }

    [Fact]
    public void CreateAIFunction_UsesStableToolContract()
    {
        CurrentDateTimeTool tool = new(TimeProvider.System);

        AIFunction function = tool.CreateAIFunction();

        Assert.Equal(CurrentDateTimeTool.FunctionName, function.Name);
        Assert.Contains("required time zone", function.Description);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
