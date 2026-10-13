namespace Norvia.Simulator.Domain.Runs;

/// <summary>The guards on a run: the most orders it may hold and the most requests it may have in flight (settings, with lower local defaults).</summary>
public sealed record SimulationLimits(int MaxOrdersPerRun, int MaxConcurrentRequests);
