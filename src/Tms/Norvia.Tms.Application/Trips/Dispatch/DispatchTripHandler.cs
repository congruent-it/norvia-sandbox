using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Dispatch;

public sealed class DispatchTripHandler(
    IRepository<Trip> trips,
    IRepository<Shipment> shipments,
    TripPlanningChecks checks,
    TripShipments tripShipments,
    IVehicleDirectory vehicles,
    IDriverDirectory drivers,
    TimeProvider time) : ICommandHandler<DispatchTripCommand, Result<DispatchTripResult>>
{
    public async ValueTask<Result<DispatchTripResult>> Handle(DispatchTripCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await trips.FindAsync(command.Number, cancellationToken) is not { } trip)
        {
            return TripRefused.Because<DispatchTripResult>(TripRefusal.TripNotFound(command.Number));
        }

        if (trip.EnsureDraft() is { } notDraft)
        {
            return TripRefused.Because<DispatchTripResult>(notDraft);
        }

        if (trip.MissingParts() is { Count: > 0 } missing)
        {
            return Result<DispatchTripResult>.Invalid(
                [.. missing.Select(part => TripRefusal.TripIncomplete(trip.Number, part)).Select(refusal => new ValidationError("Trip", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error))]);
        }

        // A Draft may have become invalid since it was built, for example after an Absence was recorded on its Driver, so every rule is checked
        // again, the Shipment rules included.
        var onTrip = await tripShipments.OfAsync([.. trip.ShipmentIds], cancellationToken);
        var check = await checks.CheckAsync(trip.Day, trip.VehicleId, trip.DriverId, trip.PlannedStart, trip.PlannedDrivingDuration, trip, onTrip, onTrip, cancellationToken);
        if (!check.IsOk)
        {
            return TripRefused.Because<DispatchTripResult>(check);
        }

        // Active Vehicles and Drivers only: the availability rules refuse an inactive one, so both are found once the check has passed.
        var vehicle = vehicles.ListActive().First(candidate => candidate.Id == trip.VehicleId);
        var driver = drivers.ListActive().First(candidate => candidate.Id == trip.DriverId);

        var ids = onTrip.Select(item => item.Shipment.Id).ToList();
        var tracked = (await shipments.ListAsync(new ShipmentsToDispatchSpec(ids), cancellationToken)).ToDictionary(shipment => shipment.Id);
        foreach (var shipment in tracked.Values)
        {
            if (!shipment.Dispatch())
            {
                throw new InvalidOperationException($"Shipment {shipment.Number} passed the Trip rules but wasn't dispatched.");
            }
        }

        var correlationId = new CorrelationId(command.CorrelationId);
        trip.Dispatch(correlationId, time.GetUtcNow(), vehicle, driver, [.. onTrip.Select(item => new DispatchedShipment(tracked[item.Shipment.Id], item.Details))]);

        // One save writes the Trip, its Shipments and TripDispatched's outbox message together.
        await trips.SaveChangesAsync(cancellationToken);
        return new DispatchTripResult(trip.Number.Value, trip.Version, correlationId.Value);
    }
}
