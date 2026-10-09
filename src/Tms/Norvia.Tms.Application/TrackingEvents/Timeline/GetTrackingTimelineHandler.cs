using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Exceptions.Timeline;
using Norvia.Tms.Application.ProofsOfDelivery.Read;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TrackingEvents.Timeline;

public sealed class GetTrackingTimelineHandler(
    IReadRepository<Shipment> shipments,
    IReadRepository<TrackingEvent> trackingEvents,
    IReadRepository<ProofOfDelivery> proofsOfDelivery,
    IReadRepository<ExceptionCase> exceptions,
    IReadRepository<ShipperNotification> notifications,
    IReadRepository<CarrierBooking> carrierBookings,
    IReadRepository<TransportOrder> transportOrders,
    ICarrierDirectory carriers)
    : IQueryHandler<GetTrackingTimelineQuery, Result<ShipmentTimeline>>
{
    public async ValueTask<Result<ShipmentTimeline>> Handle(GetTrackingTimelineQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!ShipmentNumber.TryParse(query.ShipmentNumber, out var number))
        {
            return Result.NotFound();
        }

        var shipment = await shipments.FirstOrDefaultAsync(new ShipmentByNumberSpec(number), cancellationToken);
        if (shipment is null)
        {
            return Result.NotFound();
        }

        var events = await trackingEvents.ListAsync(new TrackingTimelineSpec(shipment.Id), cancellationToken);
        var proofs = await proofsOfDelivery.ListAsync(new ProofsOfDeliveryForShipmentSpec(shipment.Id), cancellationToken);
        var shipmentExceptions = await exceptions.ListAsync(new ExceptionsOfShipmentSpec(shipment.Id), cancellationToken);
        var shipperNotifications = shipmentExceptions.Count == 0
            ? []
            : await notifications.ListAsync(new ShipperNotificationsOfExceptionsSpec([.. shipmentExceptions.Select(exception => exception.Id)]), cancellationToken);
        var bookings = await carrierBookings.ListAsync(new CarrierBookingsByShipmentSpec(shipment.Id), cancellationToken);
        var cancelledBookings = shipmentExceptions.Count == 0
            ? []
            : bookings.Where(booking => booking.CancelledAt is not null).ToList();
        var order = await transportOrders.FirstOrDefaultAsync(new TransportOrderByIdSpec(shipment.TransportOrderId), cancellationToken);
        return new ShipmentTimeline(
            [.. events.Select(TrackingTimelineEntry.Of)],
            [.. proofs.Select(ProofOfDeliveryView.Of)],
            [.. shipmentExceptions.Select(exception => ExceptionTimelineEntry.Of(exception, shipperNotifications, cancelledBookings))],
            [.. shipment.AgreedDeliveryWindows.Select(AgreedDeliveryWindowView.Of)],
            order is null ? null : ShipmentBillingView.Of(order),
            [.. bookings.Select(booking => CarrierBookingView.Of(booking, carriers))]);
    }
}
