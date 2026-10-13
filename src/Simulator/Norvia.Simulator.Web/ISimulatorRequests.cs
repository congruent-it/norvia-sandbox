using Ardalis.Result;
using Norvia.Simulator.Application.Configure;
using Norvia.Simulator.Application.Log.List;
using Norvia.Simulator.Application.Plans.Preview;
using Norvia.Simulator.Application.Runs;
using Norvia.Simulator.Application.Runs.Open;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web;

/// <summary>What the Simulator's pages ask of the application.</summary>
public interface ISimulatorRequests
{
    Task<ConfigureView> GetConfigureAsync(CancellationToken cancellationToken);

    Task<Result<PlanPreview>> PreviewAsync(SimulationSettings settings, CancellationToken cancellationToken);

    Task<Result<RunSummary>> StartAsync(SimulationSettings settings, string startedBy, CancellationToken cancellationToken);

    Task<Result> CancelAsync(SimulationRunId runId, CancellationToken cancellationToken);

    Task<Result> ClearHistoryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RunSummary>> ListRunsAsync(CancellationToken cancellationToken);

    Task<Result<RunDetail>> GetRunAsync(SimulationRunId runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LogLine>> ListLogAsync(SimulationRunId runId, LogFilter filter, CancellationToken cancellationToken);
}
