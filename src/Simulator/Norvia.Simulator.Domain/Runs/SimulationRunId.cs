namespace Norvia.Simulator.Domain.Runs;

public readonly record struct SimulationRunId(Guid Value)
{
    public static SimulationRunId New() => new(Guid.CreateVersion7());
}
