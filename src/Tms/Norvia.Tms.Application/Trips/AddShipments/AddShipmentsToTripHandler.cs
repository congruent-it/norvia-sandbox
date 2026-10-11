using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.AddShipments;

public sealed class AddShipmentsToTripHandler(IRepository<Trip> trips, TripPlanningChecks checks, TripShipments shipments, TripAmendments amendments)
    : ICommandHandler<AddShipmentsToTripCommand, Result<AddShipmentsToTripResult>>
{
    public async ValueTask<Result<AddShipmentsToTripResult>> Handle(AddShipmentsToTripCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await trips.FindAsync(command.Number, cancellationToken) is not { } trip)
        {
            return TripRefused.Because<AddShipmentsToTripResult>(TripRefusal.TripNotFound(command.Number));
        }

        if (trip.EnsureAmendable() is { } underWay)
        {
            return TripRefused.Because<AddShipmentsToTripResult>(underWay);
        }

        var (found, notFound) = await shipments.FindAsync(command.ShipmentNumbers, cancellationToken);
        var added = found.Where(item => !trip.HasShipment(item.Shipment.Id)).ToList();
        var all = await shipments.OfAsync([.. trip.ShipmentIds, .. added.Select(item => item.Shipment.Id)], cancellationToken);
        var check = await checks.CheckAsync(
            trip.Day, trip.VehicleId, trip.DriverId, trip.PlannedStart, trip.PlannedDrivingDuration, trip, all, added, cancellationToken);
        if (notFound.Count > 0 || !check.IsOk)
        {
            return TripRefused.Because<AddShipmentsToTripResult>(check.WithRefusalsFirst(notFound));
        }

        if (added.Count > 0)
        {
            var parties = trip.Status == TripStatus.Dispatched ? amendments.PartiesOf(trip.VehicleId, trip.DriverId) : null;
            if (trip.Status == TripStatus.Dispatched && parties is null)
            {
                return TripRefused.Because<AddShipmentsToTripResult>(TripRefusal.VehicleOrDriverInactive(trip.Number));
            }

            foreach (var item in added)
            {
                trip.AddShipment(item.Shipment.Id);
            }

            if (parties is not null)
            {
                await amendments.SendAsync(trip, parties, new CorrelationId(command.CorrelationId ?? CorrelationId.New().Value), cancellationToken);
            }

            await trips.SaveChangesAsync(cancellationToken);
        }

        return new AddShipmentsToTripResult(trip.Number.Value, check.Warnings);
    }
}
