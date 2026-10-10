namespace Norvia.DriverApp.Domain.Trips;

/// <summary>What a query reads of a <see cref="DriverTrip"/>, so no aggregate leaves the Domain and Infrastructure for a read.</summary>
public sealed record DriverTripRow(TripNumber TripNumber, int Version, DriverId DriverId, TripDetails Details);
