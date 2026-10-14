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
    /// <summary>Plans the accepted orders the plan chose. True when the run was cancelled before it finished.</summary>
    public async Task<bool> RunAsync(
        SimulationRun run, IReadOnlyList<AcceptedOrder> accepted, SimulationWorld world, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        var choices = run.Plan.Choices.ToDictionary(choice => choice.Step);
        var orders = accepted.Where(order => choices.ContainsKey(order.Booking.Step)).OrderBy(order => order.Booking.Step).ToList();
        if (orders.Count == 0)
        {
            return false;
        }

        var carriers = world.ActiveCarriers;
        var days = await tms.GetOwnFleetDaysAsync(cancellationToken);
        if (days is null)
        {
            await writer.NoteAsync(time.GetUtcNow(), "The TMS couldn't tell the Own fleet's capacity: the Planner assigns to Carriers only", cancellationToken);
        }

        var ledger = new FleetLedger(days ?? []);
        foreach (var order in orders)
        {
            if (stopSending.IsCancellationRequested)
            {
                return true;
            }

            await PlanAsync(run, order, choices[order.Booking.Step], carriers, ledger, writer, cancellationToken);
        }

        return false;
    }

    private async Task PlanAsync(
        SimulationRun run, AcceptedOrder order, PlannerChoice choice, IReadOnlyList<SimulatedCarrier> carriers, FleetLedger ledger, RunLogWriter writer,
        CancellationToken cancellationToken)
    {
        var booking = order.Booking;
        var day = DateOnly.FromDateTime(booking.Slot.Resolve(time.GetUtcNow()).From);
        var (assignee, reserved, intent) = Choose(choice, booking, day, carriers, ledger);

        var assigned = await tms.AssignAsync(order.TransportOrderNumber, assignee, $"SIM-{run.Number}-{booking.Step}-assign", cancellationToken);
        await writer.PlanningAsync(time.GetUtcNow(), "Assign Transport Order", booking, order.TransportOrderNumber, assigned, intent, cancellationToken);
        if (assigned.Outcome != LogOutcome.Planned || assigned.ShipmentNumber is null)
        {
            if (reserved)
            {
                ledger.Release(day, booking.TotalWeightKg, booking.PalletPlaces);
            }

            return;
        }

        if (!choice.Reassign)
        {
            return;
        }

        var target = choice.ReassignTarget(assignee, carriers);
        var reassigned = await tms.ReassignAsync(assigned.ShipmentNumber, target, $"SIM-{run.Number}-{booking.Step}-reassign", cancellationToken);
        await writer.PlanningAsync(time.GetUtcNow(), "Reassign Shipment", booking, order.TransportOrderNumber, reassigned, $"To {target.Name}", cancellationToken);
        if (reassigned.Outcome == LogOutcome.Reassigned && reserved && !target.IsOwnFleet)
        {
            ledger.Release(day, booking.TotalWeightKg, booking.PalletPlaces);
        }
    }

    // Own fleet where the order fits, a Carrier otherwise. With no active Carrier to turn to, Own fleet: the TMS only warns there.
    private static (Assignee Assignee, bool Reserved, string Intent) Choose(
        PlannerChoice choice, PlannedBooking booking, DateOnly day, IReadOnlyList<SimulatedCarrier> carriers, FleetLedger ledger)
    {
        if (choice.Intent == PlannerIntent.OwnFleetIfItFits)
        {
            if (ledger.TryReserve(day, booking.TotalWeightKg, booking.PalletPlaces))
            {
                return (Assignee.OwnFleet, true, "Own fleet has room that day");
            }

            return choice.Carrier(carriers) is { } carrier
                ? (carrier, false, $"Own fleet has no room that day: {carrier.Name}")
                : (Assignee.OwnFleet, false, "Own fleet has no room that day, and there is no Carrier to turn to");
        }

        return choice.Carrier(carriers) is { } chosen
            ? (chosen, false, $"To {chosen.Name}")
            : (Assignee.OwnFleet, false, "There is no Carrier to turn to");
    }
}
