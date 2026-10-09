using Microsoft.Extensions.Options;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.RateCards;

/// <summary>
/// The TMS's read copy of the Shippers' Rate Cards, loaded once from the seed file, which plays the ERP until its sync
/// exists (ADR 0006). A Shipper may have no Rate Card; one with two, or a Rate Card version whose Zones overlap, is a
/// broken seed, and the TMS refuses to start on it (<see cref="Seed.LoadSeedOnStart"/>).
/// </summary>
internal sealed class SeedFileRateCardDirectory : IRateCardDirectory
{
    private readonly Dictionary<ShipperId, RateCard> _rateCards;

    public SeedFileRateCardDirectory(IOptions<SeedOptions> options)
    {
        var seed = SeedFileReader.Read<SeedFile>(options);

        _rateCards = (seed.RateCards ?? [])
            .Select(rateCard => ToRateCard(rateCard, options.Value.Path))
            .ToDictionary(rateCard => rateCard.ShipperId);
    }

    public RateCard? FindFor(ShipperId shipperId) => _rateCards.GetValueOrDefault(shipperId);

    private static RateCard ToRateCard(SeedRateCard rateCard, string path) => new(
        new RateCardId(rateCard.Id),
        new ShipperId(rateCard.ShipperId),
        [.. rateCard.Versions.Select(version => SeedRateCardVersionMapper.ToVersion(version, RateCardKind.RateCard, rateCard.Id, path))]);

    private sealed record SeedFile(IReadOnlyList<SeedRateCard>? RateCards);

    private sealed record SeedRateCard(string Id, string ShipperId, IReadOnlyList<SeedRateCardVersion> Versions);
}
