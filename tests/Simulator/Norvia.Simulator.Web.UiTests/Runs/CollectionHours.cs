namespace Norvia.Simulator.Web.UiTests.Runs;

/// <summary>
/// When "Collected today" still means today. A booking's collection window starts no earlier than the next full hour and no later than 18:00 in Norvia's time,
/// after which it moves to tomorrow, when the Own fleet's Drivers are not at work (the fixture rosters only today) and no Trip is worked. The journeys that
/// have Drivers work a Trip of today run before that.
/// </summary>
public static class CollectionHours
{
    private const string Zone = "Europe/Amsterdam";

    // An hour before the last start, so a run that starts just before it doesn't cross it.
    private const int LastHour = 16;

    public const string Reason = "The Own fleet's Drivers work only today, and after 17:00 in Norvia's time a booking for today is collected tomorrow.";

    public static bool TodayIsStillOpen => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(Zone)).Hour <= LastHour;
}
