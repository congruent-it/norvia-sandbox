using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Shipments.ListAwaitingAnswer;

public sealed class ListCarrierBookingsAwaitingAnswerHandler(
    IReadRepository<CarrierBooking> carrierBookings,
    IReadRepository<Shipment> shipments,
    IReadRepository<TransportOrder> transportOrders,
    ICarrierDirectory carriers,
    CarrierBookingOverdueRules rules,
    TimeProvider time)
    : IQueryHandler<ListCarrierBookingsAwaitingAnswerQuery, IReadOnlyList<CarrierBookingAwaitingAnswerSummary>>
{
    public async ValueTask<IReadOnlyList<CarrierBookingAwaitingAnswerSummary>> Handle(
        ListCarrierBookingsAwaitingAnswerQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var requested = await carrierBookings.ListAsync(new RequestedCarrierBookingsSpec(), cancellationToken);
        if (requested.Count == 0)
        {
            return [];
        }

        var shipmentsById = (await shipments.ListAsync(new ShipmentsByIdsSpec([.. requested.Select(booking => booking.ShipmentId)]), cancellationToken))
            .ToDictionary(shipment => shipment.Id);
        var orderNumbers = shipmentsById.Values.Select(shipment => shipment.TransportOrderNumber).Distinct().ToList();
        var earliestByOrderNumber = (await transportOrders.ListAsync(new TransportOrdersByNumbersSpec(orderNumbers), cancellationToken))
            .ToDictionary(order => order.Number!, order => order.Details.Collection?.Window?.Earliest);

        var now = time.GetUtcNow();
        return [.. requested
            .Select(booking => Summarize(booking, ShipmentOf(booking, shipmentsById), earliestByOrderNumber, now))
            .OrderBy(summary => summary.EarliestCollectionTime ?? DateTimeOffset.MaxValue)];
    }

    private CarrierBookingAwaitingAnswerSummary Summarize(
        CarrierBooking booking, Shipment shipment, Dictionary<TransportOrderNumber, DateTimeOffset?> earliestByOrderNumber, DateTimeOffset now)
    {
        var earliest = earliestByOrderNumber.GetValueOrDefault(shipment.TransportOrderNumber);
        return new(
            shipment.Number.Value,
            booking.Number.Value,
            CarrierView.Of(booking.CarrierId, carriers),
            booking.RequestedAt,
            booking.SentAt,
            earliest,
            rules.IsOverdue(booking.RequestedAt, earliest, now),
            CarrierCostView.From(booking.CarrierCost));
    }

    private static Shipment ShipmentOf(CarrierBooking booking, Dictionary<ShipmentId, Shipment> shipmentsById) =>
        shipmentsById.TryGetValue(booking.ShipmentId, out var shipment)
            ? shipment
            : throw new InvalidOperationException($"Carrier booking {booking.Number} names Shipment {booking.ShipmentNumber}, which the TMS doesn't have.");
}
