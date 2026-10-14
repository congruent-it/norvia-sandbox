using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.Planner;

/// <summary>
/// What the Planner means to do with the Transport Order a booking becomes, made with the plan so the same seed gives the
/// same choices. The picks are numbers, not Carriers: they index the active Carriers when the Planner acts, so the choice holds
/// whichever Carriers the seed file has.
/// </summary>
public sealed record PlannerChoice(int Step, PlannerIntent Intent, int CarrierPick, bool Reassign, int ReassignPick)
{
    /// <summary>The Carrier this choice assigns to, or null when there is none to assign to.</summary>
    public Assignee? Carrier(IReadOnlyList<SimulatedCarrier> activeCarriers)
    {
        ArgumentNullException.ThrowIfNull(activeCarriers);
        return activeCarriers.Count == 0 ? null : Assignee.Of(activeCarriers[CarrierPick % activeCarriers.Count]);
    }

    /// <summary>
    /// Who the Planner reassigns to: from Own fleet to a Carrier, from a Carrier to another one, or to Own fleet when that
    /// Carrier is the only one. Never the current assignee, which the TMS refuses as unchanged.
    /// </summary>
    public Assignee ReassignTarget(Assignee current, IReadOnlyList<SimulatedCarrier> activeCarriers)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(activeCarriers);

        var others = activeCarriers.Where(carrier => carrier.Id != current.CarrierId).ToList();
        return others.Count == 0 ? Assignee.OwnFleet : Assignee.Of(others[ReassignPick % others.Count]);
    }
}
