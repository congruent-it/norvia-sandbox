namespace Norvia.Simulator.Domain.Planner;

/// <summary>What the Planner's Trip calls are called: the log's action, and the span the Simulator sends the call in, which are the TMS UI's names for the same actions. And the idempotency key of the Draft Trip call, the same for every retry of it.</summary>
public static class TripActions
{
    public const string CreateTrip = "Create Trip";

    public const string DispatchTrip = "Dispatch Trip";

    public static string CreationKey(int runNumber, int day, int trip) => $"SIM-{runNumber}-{day}-{trip}-trip";
}
