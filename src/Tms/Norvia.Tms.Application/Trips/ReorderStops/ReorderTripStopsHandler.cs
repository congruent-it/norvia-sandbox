using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Shipments;
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
        var numberOf = onTrip.ToDictionary(item => item.Shipment.Id, item => item.Shipment.Number.Value);
        if (command.Stops.Any(stop => !idOf.ContainsKey(stop.ShipmentNumber)))
        {
            return TripRefused.Because(TripRefusal.StopsNotTheTripsStops(trip.Number));
        }

        var order = command.Stops.Select(stop => (idOf[stop.ShipmentNumber], stop.Kind)).ToList();
        if (trip.ReorderStops(order, id => numberOf.GetValueOrDefault(id, id.ToString())) is { } refusal)
        {
            return TripRefused.Because(refusal);
        }

        await trips.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
