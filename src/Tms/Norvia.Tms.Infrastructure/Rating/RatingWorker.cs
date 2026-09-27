using System.Diagnostics;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TransportOrders.Rate;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Infrastructure.Rating;

/// <summary>
/// Rates the Transport Orders that are Not yet rated (P2). It sweeps when it starts, whenever an acceptance triggers
/// it, and on a timer, so an order whose trigger was missed, for example in a crash just after its acceptance, is still
/// rated. An order its Shipper's Rate Card can't price becomes Rating failed, which the sweep doesn't pick up again.
/// It doesn't consume Service Bus (ADR 0003). Each rating is traced under its intake's Message trail, with the outcome
/// Succeeded once it gives its answer, Rated or Rating failed, and Retrying when it didn't and the next sweep tries again.
/// </summary>
internal sealed partial class RatingWorker(
    IServiceScopeFactory scopes,
    RatingSignal signal,
    TimeProvider time,
    IHostEnvironment environment,
    IOptions<RatingOptions> options,
    ILogger<RatingWorker> logger) : BackgroundService
{
    private readonly ActivitySource _activities = new(environment.ApplicationName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SweepInterval, time);

        // Only one wait on the timer and on the signal may be outstanding, so the one that didn't fire is kept.
        Task<bool>? tick = null;
        Task<bool>? triggered = null;
        while (true)
        {
            await SweepAsync(stoppingToken);

            tick ??= timer.WaitForNextTickAsync(stoppingToken).AsTask();
            triggered ??= signal.WaitAsync(stoppingToken).AsTask();
            if (await Task.WhenAny(tick, triggered) == tick)
            {
                if (!await tick)
                {
                    return;
                }

                tick = null;
            }
            else
            {
                await triggered;
                triggered = null;
            }
        }
    }

    public override void Dispose()
    {
        _activities.Dispose();
        base.Dispose();
    }

    // Orders leave the Not yet rated list as the sweep rates them, Rated or Rating failed, so each batch passes over only
    // the orders the sweep tried and couldn't rate, because rating them threw. However many of those wait, every order
    // behind them is still reached.
    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        var notRated = 0;
        IReadOnlyList<TransportOrderToRate> batch;
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                batch = await scope.ServiceProvider.GetRequiredService<IMediator>()
                    .Send(new ListTransportOrdersNotYetRatedQuery(notRated, options.Value.BatchSize), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogSweepFailed(exception);
                return;
            }

            foreach (var order in batch)
            {
                if (!await RateAsync(order, cancellationToken))
                {
                    notRated++;
                }
            }
        }
        while (batch.Count == options.Value.BatchSize);
    }

    /// <summary>
    /// Rates one Transport Order, and says whether it has left Not yet rated, Rated or Rating failed, by this TMS or
    /// another.
    /// </summary>
    private async Task<bool> RateAsync(TransportOrderToRate order, CancellationToken cancellationToken)
    {
        var trail = new Dictionary<string, object?>
        {
            [TrackingKeys.CorrelationId] = order.CorrelationId,
            [TrackingKeys.Flow] = Flows.TransportOrderRating,
            [TrackingKeys.PartyId] = order.ShipperId,
            [TrackingKeys.TransportOrderNumber] = order.TransportOrderNumber,
        };
        using var activity = _activities.StartActivity("Rate Transport Order", ActivityKind.Internal, parentContext: default, tags: trail);
        using var scope = logger.BeginScope(trail);

        try
        {
            // A scope of its own per Transport Order, so a failed rating leaves nothing tracked for the next.
            await using var services = scopes.CreateAsyncScope();
            var result = await services.ServiceProvider.GetRequiredService<IMediator>()
                .Send(new RateTransportOrderCommand(order.TransportOrderNumber), cancellationToken);

            switch (result.IsSuccess ? result.Value : null)
            {
                case { RatingResult: RatingResult.Rated }:
                    using (TrackOutcome(activity, TrackingKeys.Succeeded))
                    {
                        LogRated(order.TransportOrderNumber);
                    }

                    return true;
                case { RatingResult: RatingResult.RatingFailed, RatingFailure: { } failure }:
                    // The order stays Accepted and can be planned, so Rating still succeeded. The failure code joins the
                    // trail's keys.
                    using (TrackOutcome(activity, TrackingKeys.Succeeded, failure.Code))
                    {
                        LogRatingFailed(order.TransportOrderNumber, failure.Code, failure.Message);
                    }

                    return true;
                default:
                    using (TrackOutcome(activity, TrackingKeys.Retrying))
                    {
                        LogNotRated(order.TransportOrderNumber);
                    }

                    return false;
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another TMS instance rated it first, and published its one outcome event.
            LogRatedElsewhere(order.TransportOrderNumber);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            using (TrackOutcome(activity, TrackingKeys.Retrying))
            {
                LogRatingThrew(exception, order.TransportOrderNumber);
            }

            return false;
        }
    }

    /// <summary>Adds a rating's outcome to the trail, with the failure code of a rating that failed.</summary>
    private IDisposable? TrackOutcome(Activity? activity, string outcome, string? failureCode = null)
    {
        // A key that doesn't apply is left out, never sent empty.
        var keys = new Dictionary<string, object?> { [TrackingKeys.Outcome] = outcome };
        if (failureCode is not null)
        {
            keys[TrackingKeys.FailureCode] = failureCode;
        }

        foreach (var (key, value) in keys)
        {
            activity?.SetTag(key, value);
        }

        return logger.BeginScope(keys);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} Rated")]
    private partial void LogRated(string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transport Order {TransportOrderNumber} Rating failed with {RatingFailureCode}: {RatingFailureMessage}")]
    private partial void LogRatingFailed(string transportOrderNumber, string ratingFailureCode, string ratingFailureMessage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transport Order {TransportOrderNumber} wasn't rated; it stays Not yet rated")]
    private partial void LogNotRated(string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} was rated by another TMS instance")]
    private partial void LogRatedElsewhere(string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rating Transport Order {TransportOrderNumber} failed; retrying on the next sweep")]
    private partial void LogRatingThrew(Exception exception, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Looking for Transport Orders to rate failed; retrying on the next sweep")]
    private partial void LogSweepFailed(Exception exception);
}
