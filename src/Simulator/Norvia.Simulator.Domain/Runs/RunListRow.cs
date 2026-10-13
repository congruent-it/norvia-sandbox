namespace Norvia.Simulator.Domain.Runs;

/// <summary>A run as the list of runs reads it: the scalar columns only, never its plan.</summary>
public sealed record RunListRow(
    Guid Id,
    int Number,
    SimulationRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string StartedBy,
    int Seed,
    string? PresetName,
    RunMode Mode,
    int PlannedCount);
