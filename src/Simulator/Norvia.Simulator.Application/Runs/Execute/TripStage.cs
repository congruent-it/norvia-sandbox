using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.Trips;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>
/// The Planner after the assignments: releases the Shipments that went to the Own fleet, builds Draft Trips for them day by day within the rules the TMS
/// checks (<see cref="TripBuilder"/>) and dispatches each, through the TMS API only. A Shipment that finds no room, and anything the TMS refuses, is logged
/// and stays Released; the run goes on. The Trips of today are the ones the Drivers go on to work.
/// </summary>
internal sealed class TripStage(ITmsApi tms, TimeProvider time)
{
    private readonly TripDispatcher dispatcher = new(tms, time);

    public async Task<TripResult> RunAsync(
        SimulationRun run, IReadOnlyList<OwnFleetShipment> ownFleet, SimulationWorld world, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        if (ownFleet.Count == 0 || run.Settings.TripPercent == 0)
        {
            return TripResult.None;
        }

        // Only the share asked for goes on Trips; the rest stay Planned, as the Planner left them.
        var inPlanOrder = ownFleet.OrderBy(shipment => shipment.Booking.Step).ToList();
        var onTrips = TripSelection.Pick(run.Settings.Seed, inPlanOrder.Count, run.Settings.TripPercent);
        var released = new List<OwnFleetShipment>();
        foreach (var shipment in inPlanOrder.Where((_, position) => onTrips[position]))
        {
            if (stopSending.IsCancellationRequested)
            {
                return new TripResult(true, 0, 0, []);
            }

            var booking = shipment.Booking;
            var release = await tms.ReleaseAsync(shipment.ShipmentNumber, PlannerActions.ReleaseKey(run.Number, booking.Step), cancellationToken);
            await writer.PlanningAsync(time.GetUtcNow(), PlannerActions.Release, booking, shipment.TransportOrderNumber, release, "Own fleet", cancellationToken);
            if (release.Outcome == LogOutcome.Released)
            {
                released.Add(shipment);
            }
        }

        if (released.Count == 0)
        {
            return TripResult.None;
        }

        var days = await tms.GetOwnFleetDaysAsync(cancellationToken);
        if (days is null)
        {
            await writer.NoteAsync(time.GetUtcNow(), "The TMS couldn't tell the Own fleet's Shifts: the Planner builds no Trips", cancellationToken);
            return TripResult.None;
        }

        var today = PickupSlot.Today(time.GetUtcNow());
        var built = 0;
        var dispatched = 0;
        var running = new List<RunningTrip>();
        foreach (var onDay in released.GroupBy(shipment => DateOnly.FromDateTime(shipment.CollectionFrom)).OrderBy(group => group.Key))
        {
            var build = TripBuilder.Build(
                onDay.Key,
                onDay.Select(shipment => new TripShipment(shipment.ShipmentNumber, shipment.Booking.Load, shipment.CollectionFrom)),
                world.OwnFleetVehicles, world.OwnFleetDrivers, days.FirstOrDefault(day => day.Date == onDay.Key)?.Shifts ?? []);

            foreach (var unplaced in build.Unplaced)
            {
                await writer.NoteAsync(
                    time.GetUtcNow(), $"{unplaced.ShipmentNumber} found no Vehicle and Driver left on {onDay.Key:yyyy-MM-dd}: it stays Released", cancellationToken);
            }

            for (var number = 0; number < build.Drafts.Count; number++)
            {
                if (stopSending.IsCancellationRequested)
                {
                    return new TripResult(true, built, dispatched, running);
                }

                var draft = build.Drafts[number];
                var (tripNumber, wasDispatched) = await dispatcher.DispatchAsync(draft, TripActions.CreationKey(run.Number, onDay.Key.DayNumber, number + 1), writer, cancellationToken);
                built += tripNumber is null ? 0 : 1;
                if (!wasDispatched)
                {
                    continue;
                }

                dispatched++;
                if (draft.Day == today)
                {
                    running.Add(new RunningTrip(
                        tripNumber!, draft.Driver, [.. draft.Shipments.Select(shipment => onDay.First(candidate => candidate.ShipmentNumber == shipment.ShipmentNumber))]));
                }
            }
        }

        return new TripResult(false, built, dispatched, running);
    }
}
