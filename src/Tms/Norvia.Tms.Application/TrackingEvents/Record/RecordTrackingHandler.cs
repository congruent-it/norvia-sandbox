using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TrackingEvents.Record;

public sealed class RecordTrackingHandler(
    IRepository<TrackingEvent> trackingEvents,
    IRepository<CarrierBooking> carrierBookings,
    IRepository<Shipment> shipments,
    TrackingEventRules rules,
    TimeProvider time)
    : ICommandHandler<RecordTrackingCommand, Result<RecordTrackingResult>>
{
    public async ValueTask<Result<RecordTrackingResult>> Handle(RecordTrackingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Own fleet reports through P12, which finds its Shipment by Trip, not by a Carrier booking reference.
        if (command.Source.CarrierId is not { } carrierId)
        {
            return TrackingEventRefused.Because<RecordTrackingResult>(TrackingEventRules.ShipmentNotFound(command.CarrierBookingReference));
        }

        var booking = await carrierBookings.SingleOrDefaultAsync(
            new ConfirmedCarrierBookingByReferenceSpec(carrierId, command.CarrierBookingReference), cancellationToken);
        if (booking is null)
        {
            return TrackingEventRefused.Because<RecordTrackingResult>(TrackingEventRules.ShipmentNotFound(command.CarrierBookingReference));
        }

        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentToChangeSpec(booking.ShipmentNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Carrier booking {booking.Number} names Shipment {booking.ShipmentNumber}, which the TMS doesn't have.");

        if (await trackingEvents.SingleOrDefaultAsync(new TrackingEventBySourceEventIdSpec(command.Source, command.SourceEventId), cancellationToken)
            is { } recorded)
        {
            return RecordTrackingResult.From(shipment, recorded, repeat: true);
        }

        var now = time.GetUtcNow();
        var refusal = TrackingEventRules.CheckShipment(shipment) ?? rules.CheckEventTime(command.EventTime, now);
        if (refusal is not null)
        {
            return TrackingEventRefused.Because<RecordTrackingResult>(refusal);
        }

        var trackingEvent = shipment.Record(
            command.Source,
            command.SourceEventId,
            command.CarrierBookingReference,
            command.Type,
            command.EventTime,
            command.Location,
            command.Eta,
            command.Remark,
            new CorrelationId(command.CorrelationId),
            now);

        // The tracked Shipment's move saves with the event, and the event it raises goes to the outbox, in one transaction.
        await trackingEvents.AddAsync(trackingEvent, cancellationToken);
        await trackingEvents.SaveChangesAsync(cancellationToken);
        return RecordTrackingResult.From(shipment, trackingEvent, repeat: false);
    }
}
