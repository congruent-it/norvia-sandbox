namespace Norvia.Simulator.Domain.Worlds;

/// <summary>The master data a Simulation plan is made from. It stays in the seed file (ADR 0002); the Simulator generates only transactional data.</summary>
public sealed record SimulationWorld(IReadOnlyList<SimulatedShipper> Shippers, IReadOnlyList<SimulatedCarrier>? Carriers = null)
{
    /// <summary>The Carriers the Planner can assign to, in a fixed order so a pick means the same Carrier each time.</summary>
    public IReadOnlyList<SimulatedCarrier> ActiveCarriers { get; } =
        [.. (Carriers ?? []).Where(carrier => carrier.Active).OrderBy(carrier => carrier.Id, StringComparer.Ordinal)];
}
