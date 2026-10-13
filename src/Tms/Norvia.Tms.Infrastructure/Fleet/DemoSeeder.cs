using Microsoft.Extensions.Hosting;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// A demo seeder that runs once when the TMS first starts and retries every <see cref="RetryInterval"/> until it succeeds, as the TMS may start
/// before its migrations are applied or before the planning horizon has opened the day. A seeder is tried again after any failure, so its steps
/// must change nothing when repeated.
/// </summary>
internal abstract class DemoSeeder(TimeProvider time) : BackgroundService
{
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    /// <summary>Whether the seed file holds this part of the demo; a seeder without it does nothing.</summary>
    protected abstract bool HasSeed { get; }

    protected abstract Task SeedAsync(CancellationToken cancellationToken);

    protected abstract void LogFailed(Exception exception);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!HasSeed)
        {
            return;
        }

        using var timer = new PeriodicTimer(RetryInterval, time);
        do
        {
            try
            {
                await SeedAsync(stoppingToken);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
