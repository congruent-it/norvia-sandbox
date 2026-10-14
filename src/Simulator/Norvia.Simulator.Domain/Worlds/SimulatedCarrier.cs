namespace Norvia.Simulator.Domain.Worlds;

/// <summary>A Carrier the Planner can assign to, from the seed file. Only active Carriers are chosen: the TMS refuses an inactive one.</summary>
public sealed record SimulatedCarrier(string Id, string Name, bool Active);
