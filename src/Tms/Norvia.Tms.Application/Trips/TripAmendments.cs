using Ardalis.SharedKernel;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>
/// What a change to a Dispatched Trip adds to the change itself (P11): the Trip goes out again as its next version. The handlers of the
/// Planner's edits <see cref="Prepare"/> before they change anything, so a refusal leaves the Trip as it was, change the Trip, and then
/// <see cref="SendAsync"/> it when it changed. A Draft needs none of this.
/// </summary>
public sealed class TripAmendments(
    IRepository<Shipment> shipments,
    TripShipments tripShipments,
    IVehicleDirectory vehicles,
    IDriverDirectory drivers,
    TimeProvider time)
{
    /// <summary>
    /// For a Dispatched Trip (or one In progress, which only taking a Shipment off can change) that would have this Vehicle and Driver: the <see cref="TripParties"/> it goes out with, or a refusal when either is
    /// missing or no longer active. For a Draft, neither: it has no version to send. Called before anything changes, so a refusal leaves the Trip as it was.
    /// </summary>
    public (TripParties? Parties, TripRefusal? Refusal) Prepare(Trip trip, VehicleId? vehicleId, DriverId? driverId)
    {
        ArgumentNullException.ThrowIfNull(trip);
        if (trip.Status == TripStatus.Draft)
        {
            return (null, null);
        }

        var vehicle = vehicles.ListActive().FirstOrDefault(candidate => candidate.Id == vehicleId);
        var driver = drivers.ListActive().FirstOrDefault(candidate => candidate.Id == driverId);
        return vehicle is null || driver is null ? (null, TripRefusal.VehicleOrDriverInactive(trip.Number)) : (new TripParties(vehicle, driver), null);
    }

    /// <summary>
    /// Raises the Trip's next version with the whole Trip as it stands now, and makes the Shipments just added Dispatched, under the correlation ID of
    /// the Planner's action (a new one when the caller gave none). Does nothing without <paramref name="parties"/>, that is, for a Draft. The caller saves.
    /// </summary>
    public async Task SendAsync(Trip trip, TripParties? parties, string? correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trip);
        if (parties is null)
        {
            return;
        }

        var onTrip = await tripShipments.OfAsync([.. trip.ShipmentIds], cancellationToken);
        var tracked = (await shipments.ListAsync(new ShipmentsToDispatchSpec([.. onTrip.Select(item => item.Shipment.Id)]), cancellationToken)).ToDictionary(shipment => shipment.Id);
        trip.Amend(
            new CorrelationId(correlationId ?? CorrelationId.New().Value),
            time.GetUtcNow(),
            parties.Vehicle,
            parties.Driver,
            [.. onTrip.Select(item => new DispatchedShipment(tracked[item.Shipment.Id], item.Details))]);
    }
}

/// <summary>The Vehicle and the Driver a Trip goes out with.</summary>
public sealed record TripParties(Vehicle Vehicle, Driver Driver);
