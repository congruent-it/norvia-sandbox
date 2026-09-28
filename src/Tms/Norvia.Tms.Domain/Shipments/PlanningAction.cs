namespace Norvia.Tms.Domain.Shipments;

/// <summary>What a Planner does to plan a Shipment (P3). Refusals, checks and traces word each action from here.</summary>
public enum PlanningAction
{
    /// <summary>Assigning a Transport Order to Own fleet or a Carrier, which plans its Shipment.</summary>
    Assignment,

    /// <summary>Assigning a Planned Shipment to Own fleet or another Carrier.</summary>
    Reassignment,

    /// <summary>Handing a Planned Shipment over to be carried out.</summary>
    Release,
}

public static class PlanningActionWording
{
    /// <summary>What the action does to a Shipment, as in "Only a Planned Shipment can be released".</summary>
    public static string Done(this PlanningAction action) => action switch
    {
        PlanningAction.Assignment => "assigned",
        PlanningAction.Reassignment => "reassigned",
        PlanningAction.Release => "released",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>The action opening a sentence, as in "A release needs an Idempotency-Key".</summary>
    public static string AsSubject(this PlanningAction action) => action switch
    {
        PlanningAction.Assignment => "An assignment",
        PlanningAction.Reassignment => "A reassignment",
        PlanningAction.Release => "A release",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };
}
