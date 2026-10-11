using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
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

        var onTrip = await shipments.OfAsync([.. trip.ShipmentIds], cancellationToken);
        var idOf = onTrip.ToDictionary(item => item.Shipment.Number.Value, item => item.Shipment.Id, StringComparer.Ordinal);
        if (command.Stops.Any(stop => !idOf.ContainsKey(stop.ShipmentNumber)))
        {
            return TripRefused.Because(TripRefusal.StopsNotTheTripsStops(trip.Number));
        }

        // The same order again changes nothing, so it needs no Vehicle or Driver; a new one is an Amend of a Dispatched Trip and needs both.
        var requested = command.Stops.Select(stop => (idOf[stop.ShipmentNumber], stop.Kind)).ToList();
        var changes = !requested.SequenceEqual(trip.Stops.Select(stop => (stop.ShipmentId, stop.Kind)));
        var (parties, inactive) = changes ? amendments.Prepare(trip, trip.VehicleId, trip.DriverId) : (null, null);
        if (inactive is not null)
        {
            return TripRefused.Because(inactive);
        }

        if (trip.ReorderStops(requested) is { } fault)
        {
            var number = onTrip.FirstOrDefault(item => item.Shipment.Id == fault.Shipment)?.Shipment.Number.Value ?? string.Empty;
            return TripRefused.Because(fault.Code == TripRefusalCode.CollectionBeforeDelivery
                ? TripRefusal.CollectionBeforeDelivery(number)
                : TripRefusal.StopsNotTheTripsStops(trip.Number));
        }

        await amendments.SendAsync(trip, parties, command.CorrelationId, cancellationToken);

        await trips.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
