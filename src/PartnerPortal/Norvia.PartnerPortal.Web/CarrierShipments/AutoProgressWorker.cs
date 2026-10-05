using System.Threading.Channels;
using Mediator;
using Norvia.PartnerPortal.Application.CarrierShipments.AutoProgress;

namespace Norvia.PartnerPortal.Web.CarrierShipments;

/// <summary>
/// Sends the Tracking events that Carrier Personas in auto-progress send by themselves, when their delay has passed. It asks
/// for the due events at startup, whenever a Shipment with automatic events is confirmed, and when the next one is due: it
/// never polls, so the serverless database in Azure still pauses when nothing is happening.
/// </summary>
internal sealed partial class AutoProgressWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<AutoProgressWorker> logger)
    : BackgroundService, IAutoProgressSignal
{
    /// <summary>How long to wait before asking again after the sweep itself failed, for example while the database resumes.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    private readonly Channel<bool> _confirmations = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void ShipmentConfirmed() => _confirmations.Writer.TryWrite(true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var next = await SendDueAsync(stoppingToken);
            await WaitAsync(next, stoppingToken);
        }
    }

    private async Task<DateTimeOffset?> SendDueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SendDueTrackingCommand(), cancellationToken)).NextDueAt;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogSweepFailed(exception);
            return time.GetUtcNow() + RetryAfter;
        }
    }

    // Until the next event is due, or until a Shipment is confirmed, whichever comes first.
    private async Task WaitAsync(DateTimeOffset? nextDueAt, CancellationToken cancellationToken)
    {
        using var due = nextDueAt is { } at
            ? new CancellationTokenSource(Max(at - time.GetUtcNow(), TimeSpan.Zero), time)
            : new CancellationTokenSource();
        using var either = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, due.Token);
        try
        {
            await _confirmations.Reader.WaitToReadAsync(either.Token);
            _confirmations.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The next Tracking event is due.
        }
        catch (OperationCanceledException)
        {
            // The portal is stopping.
        }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending the due Tracking events failed; trying again shortly")]
    private partial void LogSweepFailed(Exception exception);
}
