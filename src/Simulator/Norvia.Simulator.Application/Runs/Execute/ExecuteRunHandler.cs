using System.Collections.Concurrent;
using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>
/// Sends each planned booking as its Shipper's Persona, as fast as the concurrency cap allows (Burst) or spread over the
/// window (Paced), and logs how Norvia answered each. Real paths stamp real time: nothing is backdated. Once the bookings are in,
/// the simulated Planner assigns the Transport Orders Norvia accepted, the Carriers answer, track and deliver what went to them, and the Planner
/// builds and dispatches Trips for what stayed with the Own fleet, which the Drivers work for the Trips of today.
/// </summary>
public sealed class ExecuteRunHandler(
    IRepository<SimulationRun> runs,
    IRepository<LogEntry> log,
    IPartnerApi partnerApi,
    ITmsApi tmsApi,
    IWorldSource world,
    SimulationLimits limits,
    TimeProvider time,
    CarrierPacing pacing,
    IDriverApp driverApp,
    DriverPacing driverPacing,
    IErpApi erpApi,
    IShipperInbox shipperInbox,
    ICarrierInbox carrierInbox)
    : ICommandHandler<ExecuteRunCommand, Result>
{
    // The run's first line, "Run started", is sequence 1.
    private const int FirstSequenceAfterStart = 2;

    public async ValueTask<Result> Handle(ExecuteRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var run = await runs.FirstOrDefaultAsync(new RunByIdSpec(command.RunId), cancellationToken);
        if (run is not { IsActive: true })
        {
            return Result.NotFound($"There is no active run {command.RunId.Value}.");
        }

        using var writer = new RunLogWriter(log, run.Id, FirstSequenceAfterStart);
        var accepted = new ConcurrentBag<AcceptedOrder>();
        var stopped = await SendAllAsync(run, writer, accepted, command.StopSending, cancellationToken);
        var carrier = CarrierResult.None;
        var trips = TripResult.None;
        var drivers = DriverResult.None;
        var followUp = FollowUpResult.None;
        var invoicing = InvoicingResult.None;
        var carrierInvoicing = CarrierInvoicingResult.None;
        if (!stopped)
        {
            var planned = await new PlannerStage(tmsApi, time).RunAsync(run, [.. accepted], world.Load(), writer, command.StopSending, cancellationToken);
            stopped = planned.Stopped;
            if (!stopped)
            {
                carrier = await new CarrierStage(partnerApi, tmsApi, time, pacing).RunAsync(run, planned.CarrierShipments, writer, command.StopSending, cancellationToken);
                stopped = carrier.Stopped;
            }

            if (!stopped)
            {
                trips = await new TripStage(tmsApi, time).RunAsync(run, planned.OwnFleet, world.Load(), writer, command.StopSending, cancellationToken);
                stopped = trips.Stopped;
            }

            if (!stopped)
            {
                drivers = await new DriverStage(driverApp, time, driverPacing).RunAsync(run, trips.Running, writer, command.StopSending, cancellationToken);
                stopped = drivers.Stopped;
            }

            if (!stopped)
            {
                followUp = await new FollowUpStage(tmsApi, time, driverPacing).RunAsync(run, drivers.Failures, world.Load(), writer, command.StopSending, cancellationToken);
                stopped = followUp.Stopped;
            }

            // Finance has something to invoice once a Shipment was delivered, by a Carrier or by a Driver.
            if (!stopped && (carrier.Delivered > 0 || drivers.Completed > 0))
            {
                invoicing = await new InvoicingStage(erpApi, shipperInbox, time, driverPacing).RunAsync(run, ShippersOf(accepted), writer, command.StopSending, cancellationToken);
                stopped = invoicing.Stopped;
            }

            // The Carriers invoice what they delivered, and Finance decides the lines Norvia couldn't match.
            if (!stopped && carrier.Deliveries.Count > 0)
            {
                carrierInvoicing = await new CarrierInvoicingStage(partnerApi, erpApi, carrierInbox, time, driverPacing)
                    .RunAsync(run, carrier.Deliveries, writer, command.StopSending, cancellationToken);
                stopped = carrierInvoicing.Stopped;
            }
        }

        if (stopped)
        {
            await writer.NoteAsync(time.GetUtcNow(), "Run cancelled: nothing more is sent", cancellationToken);
            return Result.Success();
        }

        run.Complete(time.GetUtcNow());
        await runs.UpdateAsync(run, cancellationToken);
        await writer.NoteAsync(time.GetUtcNow(), Finished(run, carrier, trips, drivers, followUp, invoicing, carrierInvoicing), cancellationToken);
        return Result.Success();
    }

    private async Task<bool> SendAllAsync(
        SimulationRun run, RunLogWriter writer, ConcurrentBag<AcceptedOrder> accepted, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        var bookings = run.Plan.Bookings;
        var concurrency = Math.Max(1, limits.MaxConcurrentRequests);
        var spacing = run.Settings.Spacing(bookings.Count);
        var began = time.GetUtcNow();
        using var slots = new SemaphoreSlim(concurrency);
        var inFlight = new List<Task>(bookings.Count);

        try
        {
            for (var index = 0; index < bookings.Count; index++)
            {
                var wait = began + (spacing * index) - time.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, time, stopSending);
                }

                await slots.WaitAsync(stopSending);
                inFlight.Add(SendAsync(bookings[index], writer, accepted, slots, cancellationToken));
            }
        }
        catch (OperationCanceledException) when (stopSending.IsCancellationRequested)
        {
            // Cancelled: what is in flight lands below.
        }

        await Task.WhenAll(inFlight);
        return stopSending.IsCancellationRequested;
    }

    // The Shippers that booked in the run, by ID, so Finance meets them in the same order for the same seed.
    private static List<InvoicedShipper> ShippersOf(IEnumerable<AcceptedOrder> accepted) =>
    [
        .. accepted.Select(order => new InvoicedShipper(order.Booking.ShipperId, order.Booking.PersonaName))
            .DistinctBy(shipper => shipper.ShipperId)
            .OrderBy(shipper => shipper.ShipperId, StringComparer.Ordinal),
    ];

    private static string Finished(SimulationRun run, CarrierResult carrier, TripResult trips, DriverResult drivers, FollowUpResult followUp, InvoicingResult invoicing, CarrierInvoicingResult carrierInvoicing)
    {
        var sent = $"Run {run.Number} finished: {run.Plan.Bookings.Count} bookings sent";
        var planned = run.Plan.Choices.Count == 0 ? sent : $"{sent}, {run.Plan.Choices.Count} Transport Orders for the Planner";
        var carried = carrier.Released == 0 ? planned : $"{planned}, {carrier.Released} released, {carrier.Confirmed} confirmed, {carrier.Delivered} reported delivered";
        var driven = trips.Dispatched == 0 ? carried : $"{carried}, {trips.Dispatched} Trips dispatched, {drivers.Started} started and {drivers.Completed} completed by their Drivers";
        var followed = followUp.Redeliveries == 0 ? driven : $"{driven}, {followUp.Redeliveries} Re-delivery Trips dispatched";
        var invoiced = invoicing.Invoiced == 0 ? followed : $"{followed}, {invoicing.Invoiced} invoices and {invoicing.Credited} Credit notes issued, {invoicing.Received} in the Shippers' inboxes";
        return carrierInvoicing.Invoiced == 0
            ? invoiced
            : $"{invoiced}, {carrierInvoicing.Invoiced} Carrier invoices sent, {carrierInvoicing.Decided} lines decided, {carrierInvoicing.Received} outcomes on the Carriers' pages";
    }

    private async Task SendAsync(
        PlannedBooking booking, RunLogWriter writer, ConcurrentBag<AcceptedOrder> accepted, SemaphoreSlim slots, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await partnerApi.SendBookingAsync(booking, cancellationToken);
            await writer.BookingAsync(time.GetUtcNow(), booking, answer, cancellationToken);
            // Only an Accepted answer is a new Transport Order to assign: a repeat names the one that was assigned (or not) already.
            if (answer is { Outcome: LogOutcome.Accepted, TransportOrderNumber: { } number })
            {
                accepted.Add(new AcceptedOrder(booking, number));
            }
        }
        finally
        {
            slots.Release();
        }
    }
}
