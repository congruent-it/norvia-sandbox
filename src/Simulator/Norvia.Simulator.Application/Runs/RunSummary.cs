using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs;

/// <param name="Sent">How many bookings Norvia has answered.</param>
/// <param name="Assigned">How many Transport Orders the Planner has planned a Shipment for.</param>
/// <param name="Window">How long a paced run is spread over. Null for a list row, which doesn't load the plan, and for a burst.</param>
/// <param name="ToAssign">How many Transport Orders the plan has the Planner assign, if Norvia accepts them all. Zero for a list row, which doesn't load the plan.</param>
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
    int Sent,
    int Assigned = 0,
    int ToAssign = 0,
    TimeSpan? Window = null)
{
    public bool IsActive => Status == SimulationRunStatus.Running;

    internal static RunSummary From(SimulationRun run, int sent, int assigned = 0) => new(
        run.Id.Value, run.Number, run.Status, run.StartedAt, run.FinishedAt, run.StartedBy,
        run.Seed, run.PresetName, run.Mode, run.PlannedCount, sent, assigned, run.Plan.Choices.Count,
        run.Mode == RunMode.Paced ? run.Settings.Window : null);

    internal static RunSummary From(RunListRow row, int sent) => new(
        row.Id, row.Number, row.Status, row.StartedAt, row.FinishedAt, row.StartedBy,
        row.Seed, row.PresetName, row.Mode, row.PlannedCount, sent);
}
