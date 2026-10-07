using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TrackingEvents.Record;

public sealed class RecordTrackingHandler(
    IRepository<TrackingEvent> trackingEvents,
    IRepository<CarrierBooking> carrierBookings,
    IRepository<Shipment> shipments,
    IReadRepository<TransportOrder> transportOrders,
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

        // Own fleet reports through P12, which finds its Shipment by Trip, not by a Carrier booking reference.
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

        var correlationId = new CorrelationId(command.CorrelationId);
        var trackingEvent = shipment.Record(report with { CarrierBookingReference = booking.CarrierBookingReference }, correlationId, now);

        if (ExceptionCause.From(trackingEvent, now) is { } cause)
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

        // The Shipment moving to Delivered can complete its Transport Order's conditions to be Billable (P9).
        if (trackingEvent is { Type: TrackingEventType.Delivered, MovedShipment: true })
        {
            await billing.CheckAsync(shipment, new BillingChange(correlationId, now, DeliveredAt: trackingEvent.EventTime), cancellationToken);
        }

        // The tracked Shipment's move, the Exception the event opened and the Transport Order becoming Billable save with the
        // event, and the events they raise go to the outbox, in one transaction.
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
