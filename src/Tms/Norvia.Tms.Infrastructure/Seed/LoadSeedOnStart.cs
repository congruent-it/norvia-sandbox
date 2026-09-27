using Microsoft.Extensions.Hosting;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.RateCards;

namespace Norvia.Tms.Infrastructure.Seed;

/// <summary>
/// Loads the Rate Cards and Carriers as the TMS starts, so a broken seed stops it there rather than failing the first
/// Rating or assignment. Taking the directories is enough: each reads the seed file when it is created.
/// </summary>
internal sealed class LoadSeedOnStart(IRateCardDirectory rateCards, ICarrierDirectory carriers) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = rateCards;
        _ = carriers;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
