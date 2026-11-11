using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Exceptions.Timeline;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TrackingEvents.Timeline;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>Reads one Exception with its Shipment's Tracking events, Carrier bookings and agreed windows. Reads only (ADR 0040).</summary>
public sealed class GetExceptionTimelineHandler(
    IReadRepository<ExceptionCase> exceptions,
    IReadRepository<Shipment> shipments,
    IReadRepository<TrackingEvent> trackingEvents,
    IReadRepository<ShipperNotification> notifications,
    IReadRepository<CarrierBooking> carrierBookings,
    ICarrierDirectory carriers)
    : IQueryHandler<GetExceptionTimelineQuery, Result<ExceptionTimelineForAgent>>
{
    public async ValueTask<Result<ExceptionTimelineForAgent>> Handle(GetExceptionTimelineQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!ExceptionNumber.TryParse(query.ExceptionNumber, out var number)
            || await exceptions.FirstOrDefaultAsync(new ExceptionByNumberSpec(number), cancellationToken) is not { } exception
            || await shipments.FirstOrDefaultAsync(new ShipmentByNumberSpec(exception.ShipmentNumber), cancellationToken) is not { } shipment)
        {
            return Result.NotFound();
        }

        var events = await trackingEvents.ListAsync(new TrackingTimelineSpec(shipment.Id), cancellationToken);
        var sent = await notifications.ListAsync(new ShipperNotificationsOfExceptionsSpec([exception.Id]), cancellationToken);
        var bookings = await carrierBookings.ListAsync(new CarrierBookingsByShipmentSpec(shipment.Id), cancellationToken);
        return new ExceptionTimelineForAgent(
            shipment.Number.Value,
            shipment.Status,
            CarriedByView.Of(exception.CarriedBy, carriers),
            ExceptionTimelineEntry.Of(exception, sent, bookings.Where(booking => booking.CancelledAt is not null)),
            [.. events.Select(TrackingTimelineEntry.Of)],
            [.. bookings.Select(booking => CarrierBookingView.Of(booking, carriers))],
            [.. shipment.AgreedDeliveryWindows.Select(AgreedDeliveryWindowView.Of)]);
    }
}
