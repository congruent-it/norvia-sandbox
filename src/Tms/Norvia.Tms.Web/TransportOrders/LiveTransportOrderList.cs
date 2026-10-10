namespace Norvia.Tms.Web.TransportOrders;

/// <summary>
/// Keeps a list of Transport Orders current while a page shows it, such as those waiting for planning, the Rejected
/// bookings or the rating exceptions: it reads the list, then reads it again every <see cref="LiveListOptions.RefreshInterval"/>, so one that
/// arrives meanwhile appears without a refresh. The TMS UI reads through the query side only and never consumes events
/// (ADR 0003), so it asks again.
/// </summary>
/// <param name="read">Reads the list, such as <see cref="ITransportOrderQueries.ListRejectedAsync"/>.</param>
/// <param name="listName">What the list holds, for the log, such as "the Rejected bookings".</param>
internal sealed partial class LiveTransportOrderList<TItem>(
    Func<CancellationToken, Task<IReadOnlyList<TItem>>> read,
    string listName,
    TimeProvider time,
    TimeSpan refreshInterval,
    ILogger<LiveTransportOrderList<TItem>> logger)
    : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopped = new();
    private Task _refreshing = Task.CompletedTask;

    /// <summary>Raised after every read, whether it succeeded or not.</summary>
    public event Func<Task>? Changed;

    /// <summary>The Transport Orders from the last read that succeeded; null until one has.</summary>
    public IReadOnlyList<TItem>? Items { get; private set; }

    /// <summary>True when the last read failed; <see cref="Items"/> then still holds what was read before.</summary>
    public bool LastReadFailed { get; private set; }

    /// <summary>Reads the list now rather than at the next tick, such as after the Planner changed the day it is filtered by.</summary>
    public async Task RefreshAsync()
    {
        await ReadAsync(_stopped.Token);
        if (Changed is { } changed)
        {
            await changed();
        }
    }

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
            Items = await read(cancellationToken);
            LastReadFailed = false;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Keep what the page shows and try again on the next tick: a short outage mustn't empty the list.
            LogReadFailed(exception, listName);
            LastReadFailed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopped.CancelAsync();
        await _refreshing;
        _stopped.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading {ListName} failed; trying again")]
    private partial void LogReadFailed(Exception exception, string listName);
}
