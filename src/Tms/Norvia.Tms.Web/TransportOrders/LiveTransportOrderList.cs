using Norvia.Tms.Application.TransportOrders.List;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>
/// Keeps the list of Transport Orders waiting for planning current while a page shows it: it reads the list, then
/// reads it again every <see cref="LiveListOptions.RefreshInterval"/>, so an order accepted meanwhile appears without a
/// refresh. The TMS UI reads through the query side only and never consumes events (ADR 0003), so it asks again.
/// </summary>
internal sealed partial class LiveTransportOrderList(
    ITransportOrderQueries queries,
    TimeProvider time,
    TimeSpan refreshInterval,
    ILogger<LiveTransportOrderList> logger)
    : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopped = new();
    private Task _refreshing = Task.CompletedTask;

    /// <summary>Raised after every read, whether it succeeded or not.</summary>
    public event Func<Task>? Changed;

    /// <summary>The orders from the last read that succeeded; null until one has.</summary>
    public IReadOnlyList<TransportOrderSummary>? Orders { get; private set; }

    /// <summary>True when the last read failed; <see cref="Orders"/> then still holds what was read before.</summary>
    public bool LastReadFailed { get; private set; }

    public void Start() => _refreshing = RefreshUntilStoppedAsync(_stopped.Token);

    private async Task RefreshUntilStoppedAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(refreshInterval, time);
        try
        {
            do
            {
                await ReadAsync(cancellationToken);
                if (Changed is { } changed)
                {
                    await changed();
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The page closed.
        }
    }

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Orders = await queries.ListWaitingForPlanningAsync(cancellationToken);
            LastReadFailed = false;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Keep what the Planner sees and try again on the next tick: a short outage mustn't empty the list.
            LogReadFailed(exception);
            LastReadFailed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopped.CancelAsync();
        await _refreshing;
        _stopped.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the Transport Orders waiting for planning failed; trying again")]
    private partial void LogReadFailed(Exception exception);
}
