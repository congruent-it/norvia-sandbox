using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.GetByNumber;

public sealed class GetTripHandler(
    IReadRepository<Trip> trips,
    TripShipments shipments,
    TripPlanningChecks checks,
    IVehicleDirectory vehicles,
    IDriverDirectory drivers) : IQueryHandler<GetTripQuery, Result<TripDetailView>>
{
    public async ValueTask<Result<TripDetailView>> Handle(GetTripQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await trips.FindAsync(query.Number, cancellationToken) is not { } trip)
        {
            return TripRefused.Because<TripDetailView>(TripRefusal.TripNotFound(query.Number));
        }

        var onTrip = await shipments.OfAsync([.. trip.ShipmentIds], cancellationToken);
        var byId = onTrip.ToDictionary(item => item.Shipment.Id);
        var check = await checks.CheckAsync(trip.Day, trip.VehicleId, trip.DriverId, trip.PlannedStart, trip.PlannedDrivingDuration, trip, onTrip, [], cancellationToken);

        var view = TripViews.Of(trip, onTrip, vehicles.ListActive().ToDictionary(vehicle => vehicle.Id), drivers.ListActive().ToDictionary(driver => driver.Id, driver => driver.Name));
        return new TripDetailView(
            view,
            [.. trip.Stops.Where(stop => byId.ContainsKey(stop.ShipmentId)).Select(stop => StopText.ViewOf(stop, byId[stop.ShipmentId]))],
            [
                .. check.Failures.Select(failure => new TripRuleFailureView(failure.Code.ToString(), failure.Message)),
                .. check.Refusals.Select(refusal => new TripRuleFailureView(refusal.Code.ToString(), refusal.Message)),
            ],
            check.Warnings)
        {
            Versions = [.. trip.Deliveries.OrderBy(delivery => delivery.Version).Select(delivery => new TripVersionView(delivery.Version, delivery.State, delivery.Cause, trip.EnsureCanSendAgain(delivery.Version) is null))],
        };
    }
}
