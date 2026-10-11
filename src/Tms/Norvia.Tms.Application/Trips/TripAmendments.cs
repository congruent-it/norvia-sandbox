using Ardalis.SharedKernel;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>
/// What a change to a Dispatched Trip adds to the change itself (P11): the Trip goes out again as its next version. The handlers of the
/// Planner's edits find the <see cref="TripParties"/> before they change anything, so a refusal leaves the Trip as it was, change the Trip,
/// and then <see cref="SendAsync"/> the Trip when it changed. A Draft needs none of this.
/// </summary>
public sealed class TripAmendments(
    IRepository<Shipment> shipments,
    TripShipments tripShipments,
    IVehicleDirectory vehicles,
    IDriverDirectory drivers,
    TimeProvider time)
{
    /// <summary>The Vehicle and Driver the Trip would have, as the directories name them; null when either is missing or no longer active.</summary>
    public TripParties? PartiesOf(VehicleId? vehicleId, DriverId? driverId)
    {
        var vehicle = vehicles.ListActive().FirstOrDefault(candidate => candidate.Id == vehicleId);
        var driver = drivers.ListActive().FirstOrDefault(candidate => candidate.Id == driverId);
        return vehicle is null || driver is null ? null : new TripParties(vehicle, driver);
    }

    /// <summary>Raises the Trip's next version with the whole Trip as it stands now, and makes the Shipments just added Dispatched. The caller saves.</summary>
    public async Task SendAsync(Trip trip, TripParties parties, CorrelationId correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(parties);
        ArgumentNullException.ThrowIfNull(correlationId);

        var onTrip = await tripShipments.OfAsync([.. trip.ShipmentIds], cancellationToken);
        var tracked = (await shipments.ListAsync(new ShipmentsToDispatchSpec([.. onTrip.Select(item => item.Shipment.Id)]), cancellationToken)).ToDictionary(shipment => shipment.Id);
        trip.Amend(correlationId, time.GetUtcNow(), parties.Vehicle, parties.Driver, [.. onTrip.Select(item => new DispatchedShipment(tracked[item.Shipment.Id], item.Details))]);
    }
}

/// <summary>The Vehicle and the Driver a Trip goes out with.</summary>
public sealed record TripParties(Vehicle Vehicle, Driver Driver);
