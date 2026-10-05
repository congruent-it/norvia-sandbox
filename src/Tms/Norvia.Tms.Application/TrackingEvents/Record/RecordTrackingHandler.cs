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

        var trackingEvent = shipment.Record(report with { CarrierBookingReference = booking.CarrierBookingReference }, new CorrelationId(command.CorrelationId), now);

        // The tracked Shipment's move saves with the event, and the event it raises goes to the outbox, in one transaction.
        await trackingEvents.AddAsync(trackingEvent, cancellationToken);
        await trackingEvents.SaveChangesAsync(cancellationToken);
        return RecordTrackingResult.From(shipment, trackingEvent, repeat: false);
    }
}
