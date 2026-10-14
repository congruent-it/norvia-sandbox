namespace Norvia.Simulator.Domain.Planner;

/// <summary>
/// What the Planner's calls are called: the log's action, and the span the Simulator sends the call in, which are the TMS UI's
/// names for the same actions. And the idempotency key of each call, the same for every retry of it.
/// </summary>
public static class PlannerActions
{
    public const string Assign = "Assign Transport Order";

    public const string Reassign = "Reassign Shipment";

    public static string AssignmentKey(int runNumber, int step) => $"SIM-{runNumber}-{step}-assign";

    public static string ReassignmentKey(int runNumber, int step) => $"SIM-{runNumber}-{step}-reassign";
}
