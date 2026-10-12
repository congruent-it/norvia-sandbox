namespace Norvia.DriverApp.Application.Events;

/// <summary>Stable flow names of the tracking contract (coding guidelines §7).</summary>
public static class Flows
{
    /// <summary>
    /// A Driver's work on a Dispatched Trip (P12). It starts in the Driver app, which creates a correlation ID for each action that the Driver
    /// reports Logic App continues.
    /// </summary>
    public const string TripExecution = "trip-execution";
}
