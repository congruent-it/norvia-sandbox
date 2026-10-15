using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>
/// The simulated Planner: assigns the Transport Orders Norvia accepted, through the TMS API only, one at a time in plan order, so
/// what it chooses depends on the seed and the TMS's state and never on which answer came first. It is rule-aware: it asks the TMS
/// for the Own fleet's capacity and keeps its own tally of what it has put on each day, assigns to Own fleet where the order fits
/// and to an active Carrier otherwise. What the TMS refuses is logged as an outcome and the run goes on.
/// </summary>
internal sealed class PlannerStage(ITmsApi tms, TimeProvider time)
{
    /// <summary>Plans the accepted orders the plan chose. Says whether the run was cancelled before it finished, and which Shipments went to a Carrier.</summary>
    public async Task<PlannerResult> RunAsync(
        SimulationRun run, IReadOnlyList<AcceptedOrder> accepted, SimulationWorld world, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        var choices = run.Plan.Choices.ToDictionary(choice => choice.Step);
        var orders = accepted.Where(order => choices.ContainsKey(order.Booking.Step)).OrderBy(order => order.Booking.Step).ToList();
        if (orders.Count == 0)
        {
            return new PlannerResult(false, []);
        }

        var carriers = world.ActiveCarriers;
        var days = await tms.GetOwnFleetDaysAsync(cancellationToken);
        if (days is null)
        {
            await writer.NoteAsync(time.GetUtcNow(), "The TMS couldn't tell the Own fleet's capacity: the Planner assigns to Carriers only", cancellationToken);
        }

        var ledger = new FleetLedger(days ?? []);
        var onCarriers = new List<PlannedShipment>();
        foreach (var order in orders)
        {
            if (stopSending.IsCancellationRequested)
            {
                return new PlannerResult(true, onCarriers);
            }

            if (await PlanAsync(run, order, choices[order.Booking.Step], carriers, ledger, writer, cancellationToken) is { } shipment)
            {
                onCarriers.Add(shipment);
            }
        }

        return new PlannerResult(false, onCarriers);
    }

    // The Shipment the order ended up as, when a Carrier carries it.
    private async Task<PlannedShipment?> PlanAsync(
        SimulationRun run, AcceptedOrder order, PlannerChoice choice, IReadOnlyList<SimulatedCarrier> carriers, FleetLedger ledger, RunLogWriter writer,
        CancellationToken cancellationToken)
    {
        var booking = order.Booking;
        var day = DateOnly.FromDateTime(booking.Slot.Resolve(time.GetUtcNow()).From);
        var decision = choice.Decide(booking, day, carriers, ledger);
        var (assignee, reserved, intent) = (decision.Assignee, decision.ReservedOwnFleet, decision.Reason);

        var assigned = await tms.AssignAsync(order.TransportOrderNumber, assignee, PlannerActions.AssignmentKey(run.Number, booking.Step), cancellationToken);
        await writer.PlanningAsync(time.GetUtcNow(), PlannerActions.Assign, booking, order.TransportOrderNumber, assigned, intent, cancellationToken);
        if (assigned.Outcome != LogOutcome.Planned || assigned.ShipmentNumber is null)
        {
            if (reserved)
            {
                ledger.Release(day, booking.Load);
            }

            return null;
        }

        if (!choice.Reassign)
        {
            return OnCarrier(booking, order, assigned.ShipmentNumber, assignee, carriers);
        }

        var target = choice.ReassignTarget(assignee, carriers);
        var reassigned = await tms.ReassignAsync(assigned.ShipmentNumber, target, PlannerActions.ReassignmentKey(run.Number, booking.Step), cancellationToken);
        await writer.PlanningAsync(time.GetUtcNow(), PlannerActions.Reassign, booking, order.TransportOrderNumber, reassigned, $"To {target.Name}", cancellationToken);
        if (reassigned.Outcome != LogOutcome.Reassigned)
        {
            return OnCarrier(booking, order, assigned.ShipmentNumber, assignee, carriers);
        }

        // The tally follows the order: off Own fleet gives its room back, onto it takes room (the TMS only warns when there is none).
        if (reserved && !target.IsOwnFleet)
        {
            ledger.Release(day, booking.Load);
        }
        else if (!assignee.IsOwnFleet && target.IsOwnFleet)
        {
            ledger.TryReserve(day, booking.Load);
        }

        return OnCarrier(booking, order, assigned.ShipmentNumber, target, carriers);
    }

    private static PlannedShipment? OnCarrier(
        PlannedBooking booking, AcceptedOrder order, string shipmentNumber, Assignee assignee, IReadOnlyList<SimulatedCarrier> carriers) =>
        carriers.FirstOrDefault(carrier => carrier.Id == assignee.CarrierId) is { } carried
            ? new PlannedShipment(booking, order.TransportOrderNumber, shipmentNumber, carried)
            : null;
}
