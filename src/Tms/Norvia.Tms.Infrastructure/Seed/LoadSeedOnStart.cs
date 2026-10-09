using Microsoft.Extensions.Hosting;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.RateCards;

namespace Norvia.Tms.Infrastructure.Seed;

/// <summary>
/// Loads the Rate Cards, Carrier Rate Cards and Carriers as the TMS starts, so a broken seed stops it there rather than failing the first
/// Rating, release or assignment. Taking the directories is enough: each reads the seed file when it is created.
/// </summary>
internal sealed class LoadSeedOnStart(IRateCardDirectory rateCards, ICarrierRateCardDirectory carrierRateCards, ICarrierDirectory carriers) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = rateCards;
        _ = carrierRateCards;
        _ = carriers;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
