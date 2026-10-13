namespace Norvia.Simulator.Domain.Worlds;

/// <summary>A Shipper the Simulator books for: its Persona, and the Zones and Lanes of its Rate Card, from the seed file.</summary>
public sealed record SimulatedShipper(
    string Id,
    string Name,
    string PersonaId,
    string PersonaName,
    IReadOnlyList<RateZone> Zones,
    IReadOnlyList<RateLane> Lanes);
