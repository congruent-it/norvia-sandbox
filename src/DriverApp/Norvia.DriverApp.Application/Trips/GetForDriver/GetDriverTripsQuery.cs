using Mediator;

namespace Norvia.DriverApp.Application.Trips.GetForDriver;

public sealed record GetDriverTripsQuery(string DriverId) : IQuery<IReadOnlyList<DriverTripView>>;
