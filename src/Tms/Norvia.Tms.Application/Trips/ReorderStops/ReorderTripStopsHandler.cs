using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.ReorderStops;

public sealed class ReorderTripStopsHandler(IRepository<Trip> trips, TripShipments shipments) : ICommandHandler<ReorderTripStopsCommand, Result>
{
    public async ValueTask<Result> Handle(ReorderTripStopsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await trips.FindAsync(command.Number, cancellationToken) is not { } trip)
        {
            return TripRefused.Because(TripRefusal.TripNotFound(command.Number));
        }

        if (trip.EnsureDraft() is { } notDraft)
        {
            return TripRefused.Because(notDraft);
        }

        var onTrip = await shipments.OfAsync([.. trip.ShipmentIds], cancellationToken);
        var idOf = onTrip.ToDictionary(item => item.Shipment.Number.Value, item => item.Shipment.Id, StringComparer.Ordinal);
        if (command.Stops.Any(stop => !idOf.ContainsKey(stop.ShipmentNumber)))
        {
            return TripRefused.Because(TripRefusal.StopsNotTheTripsStops(trip.Number));
        }

        if (trip.ReorderStops([.. command.Stops.Select(stop => (idOf[stop.ShipmentNumber], stop.Kind))]) is { } fault)
        {
            var number = onTrip.FirstOrDefault(item => item.Shipment.Id == fault.Shipment)?.Shipment.Number.Value ?? string.Empty;
            return TripRefused.Because(fault.Code == TripRefusalCode.CollectionBeforeDelivery
                ? TripRefusal.CollectionBeforeDelivery(number)
                : TripRefusal.StopsNotTheTripsStops(trip.Number));
        }

        await trips.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
