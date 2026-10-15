namespace Norvia.Simulator.Domain.Planner;

/// <summary>
/// What the Planner's calls are called: the log's action, and the span the Simulator sends the call in, which are the TMS UI's
/// names for the same actions. And the idempotency key of each call, the same for every retry of it.
/// </summary>
public static class PlannerActions
{
    public const string Assign = "Assign Transport Order";

    public const string Reassign = "Reassign Shipment";

    public const string Release = "Release Shipment";

    public const string FindException = "Find Exception";

    public const string CancelCarrierBooking = "Cancel Carrier booking";

    public const string ResolveException = "Resolve Exception";

    public static string AssignmentKey(int runNumber, int step) => $"SIM-{runNumber}-{step}-assign";

    public static string ReassignmentKey(int runNumber, int step) => $"SIM-{runNumber}-{step}-reassign";

    public static string ReleaseKey(int runNumber, int step) => $"SIM-{runNumber}-{step}-release";

    public static string CancellationKey(int runNumber, int step) => $"SIM-{runNumber}-{step}-cancel";

    public static string ResolutionKey(int runNumber, int step) => $"SIM-{runNumber}-{step}-resolve";
}
