namespace Norvia.DriverApp.Domain.Trips;

public sealed record StopWindow(DateTimeOffset? Earliest, DateTimeOffset? Latest);
