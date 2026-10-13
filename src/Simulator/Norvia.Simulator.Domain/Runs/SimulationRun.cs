using Ardalis.SharedKernel;
using Norvia.Simulator.Domain.Plans;

namespace Norvia.Simulator.Domain.Runs;

/// <summary>
/// A Simulation run: the plan that was made, and how far it got. Only one run is active at a time. A run can be cancelled
/// but not resumed after a restart.
/// </summary>
public sealed class SimulationRun : EntityBase<SimulationRunId>, IAggregateRoot
{
    private SimulationRun(SimulationRunId id, int number, SimulationPlan plan, DateTimeOffset startedAt, string startedBy)
    {
        Id = id;
        Number = number;
        Plan = plan;
        StartedAt = startedAt;
        StartedBy = startedBy;
        Status = SimulationRunStatus.Running;
    }

    /// <summary>The run's number, which its Shipper references carry: <c>SIM-&lt;number&gt;-&lt;n&gt;</c>.</summary>
    public int Number { get; private set; }

    public SimulationSettings Settings => Plan.Settings;

    public SimulationPlan Plan { get; private set; }

    public SimulationRunStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public string StartedBy { get; private set; }

    public bool IsActive => Status == SimulationRunStatus.Running;

    public static SimulationRun Start(int number, SimulationSettings settings, SimulationPlan plan, DateTimeOffset now, string startedBy)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(startedBy);
        return new SimulationRun(SimulationRunId.New(), number, plan, now, startedBy);
    }

    /// <summary>Everything was sent. A run cancelled meanwhile stays cancelled.</summary>
    public void Complete(DateTimeOffset now) => Finish(SimulationRunStatus.Completed, now);

    /// <summary>The run couldn't go on, for example because the Simulator was restarted.</summary>
    public void Fail(DateTimeOffset now) => Finish(SimulationRunStatus.Failed, now);

    /// <summary>Stops the run. False when it had already finished.</summary>
    public bool Cancel(DateTimeOffset now) => Finish(SimulationRunStatus.Cancelled, now);

    private bool Finish(SimulationRunStatus status, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return false;
        }

        Status = status;
        FinishedAt = now;
        return true;
    }
}
