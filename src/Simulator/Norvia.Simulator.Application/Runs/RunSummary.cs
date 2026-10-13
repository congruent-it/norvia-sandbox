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
        run.Settings.Seed, run.Settings.PresetName, run.Settings.Mode, run.Plan.Bookings.Count, sent);
}
