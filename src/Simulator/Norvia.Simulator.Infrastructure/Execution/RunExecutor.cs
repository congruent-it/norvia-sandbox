using System.Collections.Concurrent;
using System.Threading.Channels;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Application.Runs.Abandon;
using Norvia.Simulator.Application.Runs.Execute;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Infrastructure.Execution;

/// <summary>
/// Carries a started run out in the background, one at a time, and stops it when it is cancelled. A run is not resumed after
/// a restart: the runs still marked running at startup are failed.
/// </summary>
internal sealed partial class RunExecutor(IServiceScopeFactory scopes, ILogger<RunExecutor> logger)
    : BackgroundService, IRunExecution
{
    private readonly Channel<SimulationRunId> _queue = Channel.CreateUnbounded<SimulationRunId>();
    private readonly ConcurrentDictionary<SimulationRunId, CancellationTokenSource> _stops = new();

    public void Start(SimulationRunId runId)
    {
        _stops[runId] = new CancellationTokenSource();
        _queue.Writer.TryWrite(runId);
    }

    public void Cancel(SimulationRunId runId)
    {
        if (_stops.TryGetValue(runId, out var stop))
        {
            stop.Cancel();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WithMediatorAsync(mediator => mediator.Send(new AbandonActiveRunsCommand(), stoppingToken).AsTask(), stoppingToken);

        await foreach (var runId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await ExecuteAsync(runId, stoppingToken);
        }
    }

    private async Task ExecuteAsync(SimulationRunId runId, CancellationToken stoppingToken)
    {
        var stop = _stops[runId];
        try
        {
            await WithMediatorAsync(mediator => mediator.Send(new ExecuteRunCommand(runId, stop.Token), stoppingToken).AsTask(), stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            LogRunFailed(exception, runId.Value);
            await WithMediatorAsync(mediator => mediator.Send(new AbandonActiveRunsCommand(), stoppingToken).AsTask(), stoppingToken);
        }
        finally
        {
            _stops.TryRemove(runId, out _);
            stop.Dispose();
        }
    }

    private async Task WithMediatorAsync(Func<IMediator, Task> action, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        cancellationToken.ThrowIfCancellationRequested();
        await action(scope.ServiceProvider.GetRequiredService<IMediator>());
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Run {RunId} failed")]
    private partial void LogRunFailed(Exception exception, Guid runId);
}
