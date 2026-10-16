using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.Trips;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>
/// The Planner after the Drivers: finds the Exceptions their reports opened (the TMS opens them a little later, so it asks a few times, as for the Carriers'),
/// and puts the Shipments whose delivery failed, which now wait for a Re-delivery (P12), on a Trip of a later day. The Shipments whose collection failed are
/// Planned again, for the Planner to plan in the TMS UI.
/// </summary>
internal sealed class FollowUpStage(ITmsApi tms, TimeProvider time, DriverPacing pacing)
{
    private readonly TripDispatcher dispatcher = new(tms, time);

    public async Task<FollowUpResult> RunAsync(
        SimulationRun run, IReadOnlyList<ExpectedFailure> failures, SimulationWorld world, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        if (failures.Count == 0)
        {
            return FollowUpResult.None;
        }

        if (await FindExceptionsAsync(failures, writer, stopSending, cancellationToken))
        {
            return new FollowUpResult(true, 0);
        }

        var redeliveries = failures.Where(failure => failure.Kind == ExceptionKinds.DeliveryFailed).Select(failure => failure.Shipment).DistinctBy(shipment => shipment.ShipmentNumber).ToList();
        if (redeliveries.Count == 0)
        {
            return FollowUpResult.None;
        }

        return new FollowUpResult(false, await RedeliverAsync(run, redeliveries, world, writer, stopSending, cancellationToken));
    }

    private async Task<bool> FindExceptionsAsync(
        IReadOnlyList<ExpectedFailure> failures, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        var pending = failures.ToList();
        for (var attempt = 1; pending.Count > 0 && attempt <= pacing.PollAttempts; attempt++)
        {
            if (stopSending.IsCancellationRequested)
            {
                return true;
            }

            if (attempt > 1 && pacing.PollInterval > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(pacing.PollInterval, time, stopSending);
                }
                catch (OperationCanceledException) when (stopSending.IsCancellationRequested)
                {
                    return true;
                }
            }

            var open = await tms.GetOpenExceptionsAsync(cancellationToken);
            if (open is null)
            {
                await writer.NoteAsync(time.GetUtcNow(), "The TMS couldn't tell the Open Exceptions: the Planner leaves the Drivers' Exceptions as they are", cancellationToken);
                return false;
            }

            var next = new List<ExpectedFailure>();
            foreach (var wanted in pending)
            {
                if (open.FirstOrDefault(candidate => candidate.ShipmentNumber == wanted.Shipment.ShipmentNumber && Kind(candidate.Type).Equals(wanted.Kind, StringComparison.OrdinalIgnoreCase)) is not { } found)
                {
                    next.Add(wanted);
                    continue;
                }

                await writer.PlanningAsync(
                    time.GetUtcNow(), PlannerActions.FindException, wanted.Shipment.Booking, wanted.Shipment.TransportOrderNumber,
                    new PlannerAnswer(LogOutcome.ExceptionFound, null, wanted.Shipment.ShipmentNumber, null), $"{found.ExceptionNumber}: {wanted.Kind}", cancellationToken);
            }

            pending = next;
        }

        foreach (var missing in pending)
        {
            await writer.NoteAsync(time.GetUtcNow(), $"The {missing.Kind} Exception of {missing.Shipment.ShipmentNumber} didn't open in time: the Planner leaves it", cancellationToken);
        }

        return false;
    }

    // The TMS's type may read "DeliveryFailed" or "Delivery failed".
    private static string Kind(string type) => new([.. type.Where(char.IsLetter)]);

    // How many Trips were dispatched. The Re-delivery goes on the first later open day on which Drivers work; it has no collection to be on the Trip's day (P11).
    private async Task<int> RedeliverAsync(
        SimulationRun run, List<OwnFleetShipment> shipments, SimulationWorld world, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        var days = await tms.GetOwnFleetDaysAsync(cancellationToken);
        var today = Domain.Plans.PickupSlot.Today(time.GetUtcNow());
        if (days?.Where(day => day.Date > today && day.Shifts.Count > 0).OrderBy(day => day.Date).FirstOrDefault() is not { } day)
        {
            await writer.NoteAsync(time.GetUtcNow(), "No later day has Drivers at work: the Re-deliveries wait for the Planner", cancellationToken);
            return 0;
        }

        var build = TripBuilder.Build(
            day.Date, shipments.Select(shipment => new TripShipment(shipment.ShipmentNumber, shipment.Booking.Load, shipment.CollectionFrom)),
            world.OwnFleetVehicles, world.OwnFleetDrivers, day.Shifts);
        foreach (var unplaced in build.Unplaced)
        {
            await writer.NoteAsync(time.GetUtcNow(), $"{unplaced.ShipmentNumber} found no Vehicle and Driver left on {day.Date:yyyy-MM-dd} for its Re-delivery", cancellationToken);
        }

        var dispatched = 0;
        for (var number = 0; number < build.Drafts.Count && !stopSending.IsCancellationRequested; number++)
        {
            // Apart from the Trips of the first pass, which used the day numbers' own sequence numbers.
            var key = TripActions.CreationKey(run.Number, day.Date.DayNumber, 100 + number + 1);
            dispatched += (await dispatcher.DispatchAsync(build.Drafts[number], key, writer, cancellationToken)).Dispatched ? 1 : 0;
        }

        return dispatched;
    }
}
