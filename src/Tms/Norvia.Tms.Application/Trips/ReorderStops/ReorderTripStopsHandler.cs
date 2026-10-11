using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.ReorderStops;

public sealed class ReorderTripStopsHandler(IRepository<Trip> trips, TripShipments shipments, TripAmendments amendments) : ICommandHandler<ReorderTripStopsCommand, Result>
{
    public async ValueTask<Result> Handle(ReorderTripStopsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await trips.FindAsync(command.Number, cancellationToken) is not { } trip)
        {
            return TripRefused.Because(TripRefusal.TripNotFound(command.Number));
        }

        if (trip.EnsureAmendable() is { } underWay)
        {
            return TripRefused.Because(underWay);
        }

        var parties = trip.Status == TripStatus.Dispatched ? amendments.PartiesOf(trip.VehicleId, trip.DriverId) : null;
        if (trip.Status == TripStatus.Dispatched && parties is null)
        {
            return TripRefused.Because(TripRefusal.VehicleOrDriverInactive(trip.Number));
        }

        var onTrip = await shipments.OfAsync([.. trip.ShipmentIds], cancellationToken);
        var idOf = onTrip.ToDictionary(item => item.Shipment.Number.Value, item => item.Shipment.Id, StringComparer.Ordinal);
        if (command.Stops.Any(stop => !idOf.ContainsKey(stop.ShipmentNumber)))
        {
            return TripRefused.Because(TripRefusal.StopsNotTheTripsStops(trip.Number));
        }

        var before = trip.Stops;
        if (trip.ReorderStops([.. command.Stops.Select(stop => (idOf[stop.ShipmentNumber], stop.Kind))]) is { } fault)
        {
            var number = onTrip.FirstOrDefault(item => item.Shipment.Id == fault.Shipment)?.Shipment.Number.Value ?? string.Empty;
            return TripRefused.Because(fault.Code == TripRefusalCode.CollectionBeforeDelivery
                ? TripRefusal.CollectionBeforeDelivery(number)
                : TripRefusal.StopsNotTheTripsStops(trip.Number));
        }

        if (parties is not null && !before.SequenceEqual(trip.Stops))
        {
            await amendments.SendAsync(trip, parties, new CorrelationId(command.CorrelationId ?? CorrelationId.New().Value), cancellationToken);
        }

        await trips.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
