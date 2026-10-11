using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.TakeShipmentOff;

public sealed class TakeShipmentOffTripHandler(
    IRepository<Trip> trips,
    IRepository<Shipment> shipments,
    TripShipments tripShipments,
    TripAmendments amendments,
    TimeProvider time) : ICommandHandler<TakeShipmentOffTripCommand, Result<TakeShipmentOffTripResult>>
{
    public async ValueTask<Result<TakeShipmentOffTripResult>> Handle(TakeShipmentOffTripCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await trips.FindAsync(command.Number, cancellationToken) is not { } trip)
        {
            return TripRefused.Because<TakeShipmentOffTripResult>(TripRefusal.TripNotFound(command.Number));
        }

        var (found, notFound) = await tripShipments.FindAsync([command.ShipmentNumber], cancellationToken);
        if (found.Count == 0)
        {
            return TripRefused.Because<TakeShipmentOffTripResult>(notFound[0]);
        }

        // The Shipment to change is the tracked one; the details of the others only go into the next version.
        var shipment = (await shipments.ListAsync(new ShipmentsToDispatchSpec([found[0].Shipment.Id]), cancellationToken)).Single();
        if (trip.EnsureCanTakeOff(shipment) is { } refusal)
        {
            return TripRefused.Because<TakeShipmentOffTripResult>(refusal);
        }

        var (parties, inactive) = amendments.Prepare(trip, trip.VehicleId, trip.DriverId);
        if (inactive is not null)
        {
            return TripRefused.Because<TakeShipmentOffTripResult>(inactive);
        }

        var onTrip = await tripShipments.OfAsync([.. trip.ShipmentIds.Where(id => id != shipment.Id)], cancellationToken);
        var correlationId = new CorrelationId(command.CorrelationId);
        var next = parties is null ? null : new NextTripVersion(parties.Vehicle, parties.Driver, [.. onTrip.Select(item => new DispatchedShipment(item.Shipment, item.Details))]);
        trip.TakeShipmentOff(shipment, command.Reason, correlationId, time.GetUtcNow(), next);

        // One save writes the Stops, the Shipment's status, the new version and the outbox messages together.
        await trips.SaveChangesAsync(cancellationToken);
        return new TakeShipmentOffTripResult(trip.Number.Value, shipment.Number.Value, parties is null ? null : trip.Version, correlationId.Value);
    }
}
