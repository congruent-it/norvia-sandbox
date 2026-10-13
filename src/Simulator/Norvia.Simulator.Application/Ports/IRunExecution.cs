using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Ports;

/// <summary>Carries a started run out in the background, and stops it when it is cancelled.</summary>
public interface IRunExecution
{
    void Start(SimulationRunId runId);

    void Cancel(SimulationRunId runId);
}
