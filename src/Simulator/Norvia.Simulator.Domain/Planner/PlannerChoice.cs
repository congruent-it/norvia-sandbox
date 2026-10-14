using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.Planner;

/// <summary>
/// What the Planner means to do with the Transport Order a booking becomes, made with the plan so the same seed gives the
/// same choices. The draws are numbers, not Carriers: each is reduced to an index into the active Carriers when the Planner acts, so the choice holds
/// whichever Carriers the seed file has.
/// </summary>
public sealed record PlannerChoice(int Step, PlannerIntent Intent, int CarrierDraw, bool Reassign, int ReassignDraw)
{
    /// <summary>
    /// Who gets the order: Own fleet where the order fits the day's room (which it then takes from the ledger), a Carrier otherwise.
    /// With no active Carrier to turn to, Own fleet: the TMS only warns about load there.
    /// </summary>
    public PlannerDecision Decide(PlannedBooking booking, DateOnly day, IReadOnlyList<SimulatedCarrier> activeCarriers, FleetLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(ledger);

        if (Intent == PlannerIntent.OwnFleetIfItFits)
        {
            if (ledger.TryReserve(day, booking.Load))
            {
                return new PlannerDecision(Assignee.OwnFleet, ReservedOwnFleet: true, "Own fleet has room that day");
            }

            return Carrier(activeCarriers) is { } carrier
                ? new PlannerDecision(carrier, ReservedOwnFleet: false, $"Own fleet has no room that day: {carrier.Name}")
                : new PlannerDecision(Assignee.OwnFleet, ReservedOwnFleet: false, "Own fleet has no room that day, and there is no Carrier to turn to");
        }

        return Carrier(activeCarriers) is { } chosen
            ? new PlannerDecision(chosen, ReservedOwnFleet: false, $"To {chosen.Name}")
            : new PlannerDecision(Assignee.OwnFleet, ReservedOwnFleet: false, "There is no Carrier to turn to");
    }

    /// <summary>The Carrier this choice assigns to, or null when there is none to assign to.</summary>
    public Assignee? Carrier(IReadOnlyList<SimulatedCarrier> activeCarriers)
    {
        ArgumentNullException.ThrowIfNull(activeCarriers);
        return activeCarriers.Count == 0 ? null : Assignee.Of(activeCarriers[CarrierDraw % activeCarriers.Count]);
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
        return others.Count == 0 ? Assignee.OwnFleet : Assignee.Of(others[ReassignDraw % others.Count]);
    }
}
