namespace Norvia.DriverApp.Application.Events;

public static class Topics
{
    /// <summary>Where the Driver app publishes what the Driver reported (P12). The Driver reports Logic App is the only consumer.</summary>
    public const string DriverAppEvents = "driver-app-events";
}
