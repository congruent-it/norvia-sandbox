using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
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
    IRepository<ExceptionCase> exceptions,
    IExceptionNumbers exceptionNumbers,
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

        if (ExceptionTypes.OpenedBy(trackingEvent.Type) is { } exceptionType)
        {
            await OpenOrJoinExceptionAsync(shipment, exceptionType, trackingEvent, now, cancellationToken);
        }

        // The tracked Shipment's move and the Exception the event opened save with the event, and the events they raise go
        // to the outbox, in one transaction.
        await trackingEvents.AddAsync(trackingEvent, cancellationToken);
        await trackingEvents.SaveChangesAsync(cancellationToken);
        return RecordTrackingResult.From(shipment, trackingEvent, repeat: false);
    }

    // A Shipment has at most one open Exception of each type: a later cause joins it, and publishes nothing.
    private async Task OpenOrJoinExceptionAsync(Shipment shipment, ExceptionType type, TrackingEvent cause, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await exceptions.SingleOrDefaultAsync(new OpenExceptionOfTypeForShipmentSpec(shipment.Id, type), cancellationToken) is { } open)
        {
            open.AddCause(cause, now);
            return;
        }

        var number = await exceptionNumbers.NextAsync(cancellationToken);
        await exceptions.AddAsync(ExceptionCase.Open(number, shipment, cause, now), cancellationToken);
    }
}
