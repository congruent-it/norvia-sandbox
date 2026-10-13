using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs;

public sealed record RunSummary(
    Guid Id,
    int Number,
    SimulationRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string StartedBy,
    int Seed,
    string? PresetName,
    RunMode Mode,
    int Planned,
    int Sent)
{
    public bool IsActive => Status == SimulationRunStatus.Running;

    internal static RunSummary From(SimulationRun run, int sent) => new(
        run.Id.Value, run.Number, run.Status, run.StartedAt, run.FinishedAt, run.StartedBy,
        run.Seed, run.PresetName, run.Mode, run.PlannedCount, sent);

    internal static RunSummary From(RunListRow row, int sent) => new(
        row.Id, row.Number, row.Status, row.StartedAt, row.FinishedAt, row.StartedBy,
        row.Seed, row.PresetName, row.Mode, row.PlannedCount, sent);
}
