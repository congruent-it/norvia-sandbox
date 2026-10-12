using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Mediator;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.TrackingEvents.Record;

public sealed class RecordTrackingHandler(
    IRepository<TrackingEvent> trackingEvents,
    IRepository<CarrierBooking> carrierBookings,
    IRepository<Shipment> shipments,
    IReadRepository<TransportOrder> transportOrders,
    IReadRepository<Trip> trips,
    ExceptionCauses exceptionCauses,
    BillingCheck billing,
    TrackingEventRules rules,
    TimeProvider time)
    : ICommandHandler<RecordTrackingCommand, Result<RecordTrackingResult>>
{
    public async ValueTask<Result<RecordTrackingResult>> Handle(RecordTrackingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var report = command.Report;

        if (report.OwnFleet is not null)
        {
            return await HandleOwnFleetAsync(command, cancellationToken);
        }

        if (report.Source.CarrierId is not { } carrierId)
        {
            return TrackingEventRefused.Because<RecordTrackingResult>(TrackingEventRules.ShipmentNotFound(report.CarrierBookingReference ?? report.CarrierBookingNumber ?? string.Empty));
        }

        var booking = report.CarrierBookingReference is { } reference
            ? await carrierBookings.SingleOrDefaultAsync(new ConfirmedCarrierBookingByReferenceSpec(carrierId, reference), cancellationToken)
            : null;
        if (booking is null && CarrierBookingNumber.TryParse(report.CarrierBookingNumber, out var number))
        {
            booking = await carrierBookings.SingleOrDefaultAsync(new ConfirmedCarrierBookingByNumberSpec(carrierId, number), cancellationToken);
        }

        if (booking is null)
        {
            return TrackingEventRefused.Because<RecordTrackingResult>(TrackingEventRules.ShipmentNotFound(report.CarrierBookingReference ?? report.CarrierBookingNumber ?? string.Empty));
        }

        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentToChangeSpec(booking.ShipmentNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Carrier booking {booking.Number} names Shipment {booking.ShipmentNumber}, which the TMS doesn't have.");

        if (await trackingEvents.SingleOrDefaultAsync(new TrackingEventBySourceEventIdSpec(report.Source, report.EventId), cancellationToken)
            is { } recorded)
        {
            return RecordTrackingResult.From(shipment, recorded, repeat: true);
        }

        var now = time.GetUtcNow();
        var refusal = TrackingEventRules.CheckShipment(shipment) ?? rules.CheckEventTime(report.EventTime, now);
        if (refusal is not null)
        {
            return TrackingEventRefused.Because<RecordTrackingResult>(refusal);
        }

        return await RecordAsync(shipment, report with { CarrierBookingReference = booking.CarrierBookingReference }, booking, new CorrelationId(command.CorrelationId), now, cancellationToken);
    }

    // A Driver's report (P12) finds its Shipment by Trip, not by a Carrier booking reference, and the Trip's rules decide it. The Driver app
    // event ID is checked first: a repeat is answered as before, whatever has happened on the Trip since.
    private async Task<Result<RecordTrackingResult>> HandleOwnFleetAsync(RecordTrackingCommand command, CancellationToken cancellationToken)
    {
        var report = command.Report;
        var own = report.OwnFleet!;

        if (await trackingEvents.SingleOrDefaultAsync(new TrackingEventBySourceEventIdSpec(report.Source, report.EventId), cancellationToken) is { } recorded)
        {
            var repeated = await shipments.SingleOrDefaultAsync(new ShipmentToChangeSpec(recorded.ShipmentNumber), cancellationToken)
                ?? throw new InvalidOperationException($"Tracking event {recorded.SourceEventId} names Shipment {recorded.ShipmentNumber}, which the TMS doesn't have.");
            return RecordTrackingResult.From(repeated, recorded, repeat: true);
        }

        var trip = await trips.FirstOrDefaultAsync(new TripByNumberSpec(own.TripNumber), cancellationToken);
        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentToChangeSpec(own.ShipmentNumber), cancellationToken);
        var outcomes = StopOutcomes.From(trip is null ? [] : await trackingEvents.ListAsync(new TrackingEventsOfTripSpec(own.TripNumber), cancellationToken));

        var now = time.GetUtcNow();
        if (rules.CheckOwnFleetReport(trip, shipment, report, outcomes, now) is { } refusal)
        {
            return TrackingEventRefused.Because<RecordTrackingResult>(refusal);
        }

        return await RecordAsync(shipment!, report, booking: null, new CorrelationId(command.CorrelationId), now, cancellationToken);
    }

    // The part a Carrier's and a Driver's report share: the Shipment takes the event in, an exception opens or clears its Exception, and the
    // move, the Exception and the Transport Order becoming Billable save with the event, and the events they raise go to the outbox, in one
    // transaction.
    private async Task<Result<RecordTrackingResult>> RecordAsync(
        Shipment shipment, TrackingReport report, CarrierBooking? booking, CorrelationId correlationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var trackingEvent = shipment.Record(report, correlationId, now);

        var cause = ExceptionCause.From(trackingEvent, now);
        if (cause is not null)
        {
            await exceptionCauses.OpenOrAddAsync(shipment, cause, cancellationToken);
        }
        else
        {
            foreach (var cleared in ExceptionTypes.ClearedBy(trackingEvent.Type))
            {
                await exceptionCauses.ClearAsync(shipment, cleared, cancellationToken);
            }
        }

        await CheckLateDeliveryAsync(shipment, trackingEvent, now, cancellationToken);

        if (trackingEvent is { Type: TrackingEventType.Delivered, MovedShipment: true })
        {
            if (booking is not null)
            {
                // The Carrier has earned the Carrier cost of the Confirmed Carrier booking that carried the Shipment (P9).
                shipment.IncurCarrierCost(booking, trackingEvent);
            }

            // The Shipment moving to Delivered can also complete its Transport Order's conditions to be Billable (P9).
            await billing.CheckAsync(shipment, new BillingChange(correlationId, now, DeliveredAt: trackingEvent.EventTime, ExceptionOpened: cause is not null), cancellationToken);
        }

        await trackingEvents.AddAsync(trackingEvent, cancellationToken);
        await trackingEvents.SaveChangesAsync(cancellationToken);
        return RecordTrackingResult.From(shipment, trackingEvent, repeat: false);
    }

    // An ETA after the delivery window is a cause of Late delivery, and a later one inside it clears the cause. A Delivered
    // Shipment isn't checked: the Delivered event itself has cleared it.
    private async Task CheckLateDeliveryAsync(Shipment shipment, TrackingEvent trackingEvent, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (trackingEvent.Eta is not { } eta || shipment.Status == ShipmentStatus.Delivered)
        {
            return;
        }

        var order = await transportOrders.SingleOrDefaultAsync(new TransportOrderByNumberSpec(shipment.TransportOrderNumber), cancellationToken);
        if (LateDeliveryRules.IsEtaLate(eta, shipment.DeliveryWindowOf(order?.Details)))
        {
            await exceptionCauses.OpenOrAddAsync(shipment, ExceptionCause.LateEta(trackingEvent, now), cancellationToken);
        }
        else
        {
            await exceptionCauses.ClearAsync(shipment, ExceptionType.LateDelivery, cancellationToken);
        }
    }
}
