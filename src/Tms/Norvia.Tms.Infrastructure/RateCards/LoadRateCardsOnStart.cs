using Microsoft.Extensions.Hosting;
using Norvia.Tms.Application.RateCards;

namespace Norvia.Tms.Infrastructure.RateCards;

/// <summary>
/// Loads the Rate Cards as the TMS starts, so a broken seed stops it there rather than failing the first Rating.
/// Taking the directory is enough: it reads the seed file when it is created.
/// </summary>
internal sealed class LoadRateCardsOnStart(IRateCardDirectory rateCards) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = rateCards;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
