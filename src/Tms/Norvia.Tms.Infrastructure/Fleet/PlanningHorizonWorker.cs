using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Tms.Application.Fleet.Planning.OpenDays;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// The daily horizon job (P10): at 00:00 Europe/Amsterdam it opens every missing day up to the end of the Planning horizon
/// and closes the days before today. It looks every <see cref="PlanningHorizonOptions.CheckInterval"/> whether a new day has
/// begun, and when it starts, so it catches up after downtime; the work itself changes nothing when there is none to do.
/// Each run is logged, and traced in a span of its own, as it runs outside any call: Succeeded when it ran, and Retrying
/// when it failed, as the next look tries again.
/// </summary>
internal sealed partial class PlanningHorizonWorker(
    IServiceScopeFactory scopes,
    PlanningHorizonRules horizon,
    TimeProvider time,
    IHostEnvironment environment,
    IOptions<PlanningHorizonOptions> options,
    ILogger<PlanningHorizonWorker> logger) : BackgroundService
{
    private readonly ActivitySource _activities = new(environment.ApplicationName);
    private DateOnly? _lastRunDay;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CheckInterval, time);
        do
        {
            await RunIfDueAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public override void Dispose()
    {
        _activities.Dispose();
        base.Dispose();
    }

    private async Task RunIfDueAsync(CancellationToken cancellationToken)
    {
        var today = horizon.Today(time.GetUtcNow());
        if (today == _lastRunDay)
        {
            return;
        }

        using var activity = _activities.StartActivity("Open Planning horizon", ActivityKind.Internal);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new OpenPlanningDaysCommand(), cancellationToken);
            _lastRunDay = today;
            activity?.SetTag(TrackingKeys.Outcome, TrackingKeys.Succeeded);
            using var outcome = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogRan(result.Today, result.Opened.Count, result.Closed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Another TMS instance that opened the same day first lands here too: the next look finds its days there.
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag(TrackingKeys.Outcome, TrackingKeys.Retrying);
            using var outcome = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.Outcome] = TrackingKeys.Retrying });
            LogFailed(exception, today);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Planning horizon job ran for {Today:yyyy-MM-dd}: opened {OpenedCount} days, closed {ClosedCount}")]
    private partial void LogRan(DateOnly today, int openedCount, int closedCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Planning horizon job failed for {Today:yyyy-MM-dd}; retrying on the next look")]
    private partial void LogFailed(Exception exception, DateOnly today);
}
