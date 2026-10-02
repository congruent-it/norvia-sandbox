using System.Threading.Channels;
using Mediator;
using Norvia.PartnerPortal.Application.CarrierBookings.AutoAnswer;

namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>
/// Answers the booking requests that Carrier Personas answer automatically, when their delay has passed. It asks for the
/// due answers at startup, whenever a request with an automatic answer arrives, and when the next one is due: it never
/// polls, so the serverless database in Azure still pauses when nothing is happening.
/// </summary>
internal sealed partial class AutoAnswerWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<AutoAnswerWorker> logger)
    : BackgroundService, IAutoAnswerSignal
{
    /// <summary>How long to wait before asking again after the sweep itself failed, for example while the database resumes.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    private readonly Channel<bool> _arrivals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void RequestArrived() => _arrivals.Writer.TryWrite(true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var next = await AnswerDueAsync(stoppingToken);
            await WaitAsync(next, stoppingToken);
        }
    }

    private async Task<DateTimeOffset?> AnswerDueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new AnswerDueCarrierBookingsCommand(), cancellationToken)).NextDueAt;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogSweepFailed(exception);
            return time.GetUtcNow() + RetryAfter;
        }
    }

    // Until the next answer is due, or until a request arrives, whichever comes first.
    private async Task WaitAsync(DateTimeOffset? nextDueAt, CancellationToken cancellationToken)
    {
        using var due = nextDueAt is { } at
            ? new CancellationTokenSource(Max(at - time.GetUtcNow(), TimeSpan.Zero), time)
            : new CancellationTokenSource();
        using var either = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, due.Token);
        try
        {
            await _arrivals.Reader.WaitToReadAsync(either.Token);
            _arrivals.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The next answer is due.
        }
        catch (OperationCanceledException)
        {
            // The portal is stopping.
        }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    [LoggerMessage(Level = LogLevel.Error, Message = "Answering the due booking requests failed; trying again shortly")]
    private partial void LogSweepFailed(Exception exception);
}
