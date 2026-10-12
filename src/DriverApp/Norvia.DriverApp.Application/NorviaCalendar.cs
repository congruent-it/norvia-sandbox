namespace Norvia.DriverApp.Application;

/// <summary>What day it is where Norvia works: a Trip's day is its day in Norvia's time, whatever the server's clock says.</summary>
internal static class NorviaCalendar
{
    private static readonly TimeZoneInfo NorviaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, NorviaTimeZone).DateTime);
}
