using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Tools;

public sealed class CurrentDateTimeTool
{
    public const string FunctionName = "get_current_date_time";

    private readonly TimeProvider _timeProvider;

    public CurrentDateTimeTool(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public CurrentDateTimeResult GetCurrentDateTime(
        [Description("The IANA or system time-zone identifier, for example America/Argentina/Buenos_Aires or UTC.")]
        string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException("A time-zone identifier is required.", nameof(timeZoneId));
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException(
                $"The time-zone identifier '{timeZoneId}' is not available on this system.",
                nameof(timeZoneId),
                exception);
        }

        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), timeZone);

        return new CurrentDateTimeResult(
            localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            localNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            localNow.DayOfWeek.ToString(),
            timeZone.Id,
            localNow.ToString("zzz", CultureInfo.InvariantCulture));
    }

    public AIFunction CreateAIFunction() => AIFunctionFactory.Create(
        GetCurrentDateTime,
        name: FunctionName,
        description: "Gets today's date and current time in a required time zone, including the weekday and UTC offset.");
}

public sealed record CurrentDateTimeResult(
    string Date,
    string Time,
    string DayOfWeek,
    string TimeZoneId,
    string UtcOffset);
