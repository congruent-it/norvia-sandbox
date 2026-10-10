using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.SendAgain;

public sealed class SendTripAgainHandler(
    IRepository<Trip> trips,
    TripShipments tripShipments,
    IVehicleDirectory vehicles,
    IDriverDirectory drivers,
    TimeProvider time) : ICommandHandler<SendTripAgainCommand, Result<SendTripAgainResult>>
{
    public async ValueTask<Result<SendTripAgainResult>> Handle(SendTripAgainCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await trips.FindAsync(command.Number, cancellationToken) is not { } trip)
        {
            return TripRefused.Because<SendTripAgainResult>(TripRefusal.TripNotFound(command.Number));
        }

        if (trip.EnsureCanSendAgain(command.Version) is { } refusal)
        {
            return TripRefused.Because<SendTripAgainResult>(refusal);
        }

        // The Trip goes out as it stands now, with its Vehicle and Driver as the directories name them.
        var vehicle = vehicles.ListActive().FirstOrDefault(candidate => candidate.Id == trip.VehicleId);
        var driver = drivers.ListActive().FirstOrDefault(candidate => candidate.Id == trip.DriverId);
        if (vehicle is null || driver is null)
        {
            return TripRefused.Because<SendTripAgainResult>(TripRefusal.VehicleOrDriverInactive(trip.Number));
        }

        var onTrip = await tripShipments.OfAsync([.. trip.ShipmentIds], cancellationToken);
        var correlationId = new CorrelationId(command.CorrelationId);
        trip.SendAgain(command.Version, correlationId, time.GetUtcNow(), vehicle, driver, [.. onTrip.Select(item => new DispatchedShipment(item.Shipment, item.Details))]);

        // One save writes the version's state and TripDispatched's outbox message together.
        await trips.SaveChangesAsync(cancellationToken);
        return new SendTripAgainResult(trip.Number.Value, command.Version, correlationId.Value);
    }
}
