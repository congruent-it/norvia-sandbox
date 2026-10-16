using Ardalis.Result;
using Mediator;
using Norvia.Simulator.Application.Configure;
using Norvia.Simulator.Application.Log.List;
using Norvia.Simulator.Application.Plans.Preview;
using Norvia.Simulator.Application.Reset;
using Norvia.Simulator.Application.Runs;
using Norvia.Simulator.Application.Runs.Cancel;
using Norvia.Simulator.Application.Runs.ClearHistory;
using Norvia.Simulator.Application.Runs.List;
using Norvia.Simulator.Application.Runs.Open;
using Norvia.Simulator.Application.Runs.Start;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web;

/// <summary>
/// Sends the application's commands and queries, each in a scope of its own. A Blazor Server circuit lives as long as the page
/// is open, so a scope per circuit would keep one database context for all of it.
/// </summary>
internal sealed class SimulatorRequests(IServiceScopeFactory scopes) : ISimulatorRequests
{
    public Task<ConfigureView> GetConfigureAsync(CancellationToken cancellationToken) =>
        SendAsync(mediator => mediator.Send(new GetConfigureQuery(), cancellationToken));

    public Task<Result<PlanPreview>> PreviewAsync(SimulationSettings settings, CancellationToken cancellationToken) =>
        SendAsync(mediator => mediator.Send(new PreviewPlanQuery(settings), cancellationToken));

    public Task<Result<RunSummary>> StartAsync(SimulationSettings settings, string startedBy, CancellationToken cancellationToken) =>
        SendAsync(mediator => mediator.Send(new StartRunCommand(settings, startedBy), cancellationToken));

    public Task<Result> CancelAsync(SimulationRunId runId, CancellationToken cancellationToken) =>
        SendAsync(mediator => mediator.Send(new CancelRunCommand(runId), cancellationToken));

    public Task<Result> ClearHistoryAsync(CancellationToken cancellationToken) =>
        SendAsync(mediator => mediator.Send(new ClearHistoryCommand(), cancellationToken));

    public Task<EstateResetReadiness> GetResetReadinessAsync(CancellationToken cancellationToken) =>
        SendAsync(mediator => mediator.Send(new GetResetReadinessQuery(), cancellationToken));

    public Task<Result<EstateResetReport>> ResetEstateAsync(CancellationToken cancellationToken) =>
        SendAsync(mediator => mediator.Send(new ResetEstateCommand(), cancellationToken));

    public Task<IReadOnlyList<RunSummary>> ListRunsAsync(CancellationToken cancellationToken) =>
        SendAsync(mediator => mediator.Send(new ListRunsQuery(), cancellationToken));

    public Task<Result<RunDetail>> GetRunAsync(SimulationRunId runId, CancellationToken cancellationToken) =>
        SendAsync(mediator => mediator.Send(new GetRunQuery(runId), cancellationToken));

    public Task<IReadOnlyList<LogLine>> ListLogAsync(SimulationRunId runId, LogFilter filter, CancellationToken cancellationToken) =>
        SendAsync(mediator => mediator.Send(new ListLogQuery(runId, filter), cancellationToken));

    private async Task<T> SendAsync<T>(Func<IMediator, ValueTask<T>> send)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await send(scope.ServiceProvider.GetRequiredService<IMediator>());
    }
}
