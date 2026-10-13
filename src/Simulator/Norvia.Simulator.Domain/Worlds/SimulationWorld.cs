namespace Norvia.Simulator.Domain.Worlds;

/// <summary>The master data a Simulation plan is made from. It stays in the seed file (ADR 0002); the Simulator generates only transactional data.</summary>
public sealed record SimulationWorld(IReadOnlyList<SimulatedShipper> Shippers);
