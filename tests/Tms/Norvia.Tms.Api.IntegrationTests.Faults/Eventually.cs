namespace Norvia.Tms.Api.IntegrationTests.Faults;

/// <summary>Waits for what the estate does in its own time: a message on its way through a worker to the TMS.</summary>
internal static class Eventually
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Tries again until the attempt gives something, and fails the test when it doesn't within the patience.</summary>
    public static async Task<T> Async<T>(Func<Task<T?>> attempt, CancellationToken cancellationToken, TimeSpan? patience = null)
        where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(patience ?? Patience);
        try
        {
            while (true)
            {
                if (await attempt() is { } found)
                {
                    return found;
                }

                await Task.Delay(PollInterval, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("What the test waited for didn't happen in time.");
        }
    }

    /// <summary>Tries again until the condition holds.</summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken, TimeSpan? patience = null) =>
        await Async(async () => await condition() ? new object() : null, cancellationToken, patience);
}
