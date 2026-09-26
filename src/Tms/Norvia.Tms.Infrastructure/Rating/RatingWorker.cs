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
/// rated. It doesn't consume Service Bus (ADR 0003). Each rating is traced under its intake's Message trail.
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

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TransportOrderToRate> toRate;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            toRate = await scope.ServiceProvider.GetRequiredService<IMediator>()
                .Send(new ListTransportOrdersNotYetRatedQuery(options.Value.BatchSize), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSweepFailed(exception);
            return;
        }

        foreach (var order in toRate)
        {
            await RateAsync(order, cancellationToken);
        }
    }

    private async Task RateAsync(TransportOrderToRate order, CancellationToken cancellationToken)
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

            if (result.IsSuccess && result.Value == RatingResult.Rated)
            {
                LogRated(order.TransportOrderNumber);
            }
            else
            {
                // Until Rating failed exists (P2.3), an order its Rate Card can't price waits for the next sweep.
                LogNotRated(order.TransportOrderNumber);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another TMS instance rated it first, and published its one TransportOrderRated.
            LogRatedElsewhere(order.TransportOrderNumber);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            LogRatingFailed(exception, order.TransportOrderNumber);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} Rated")]
    private partial void LogRated(string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transport Order {TransportOrderNumber} can't be rated from its Shipper's Rate Card; it stays Not yet rated")]
    private partial void LogNotRated(string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} was rated by another TMS instance")]
    private partial void LogRatedElsewhere(string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rating Transport Order {TransportOrderNumber} failed; retrying on the next sweep")]
    private partial void LogRatingFailed(Exception exception, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Looking for Transport Orders to rate failed; retrying on the next sweep")]
    private partial void LogSweepFailed(Exception exception);
}
