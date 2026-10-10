namespace Norvia.Tms.Application.Trips.Choices;

public sealed record TripChoices(IReadOnlyList<VehicleChoice> Vehicles, IReadOnlyList<DriverChoice> Drivers);

public sealed record VehicleChoice(string Id, string Registration);

public sealed record DriverChoice(string Id, string Name);
