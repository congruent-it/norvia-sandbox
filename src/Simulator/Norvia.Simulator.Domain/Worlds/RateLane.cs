namespace Norvia.Simulator.Domain.Worlds;

/// <summary>A Lane of a Shipper's Rate Card, by the names of its origin and destination Zones.</summary>
public sealed record RateLane(string Origin, string Destination);
