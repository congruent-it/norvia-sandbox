using Microsoft.Extensions.Options;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.RateCards;

/// <summary>
/// The TMS's read copy of the Carriers' Carrier Rate Cards, loaded once from the seed file, which plays the ERP until its
/// sync exists (ADR 0013). A Carrier may have no Carrier Rate Card; one with two, or a version whose Zones overlap, is a
/// broken seed, and the TMS refuses to start on it (<see cref="Seed.LoadSeedOnStart"/>).
/// </summary>
internal sealed class SeedFileCarrierRateCardDirectory : ICarrierRateCardDirectory
{
    private readonly Dictionary<CarrierId, CarrierRateCard> _carrierRateCards;

    public SeedFileCarrierRateCardDirectory(IOptions<SeedOptions> options)
    {
        var seed = SeedFileReader.Read<SeedFile>(options);

        _carrierRateCards = (seed.CarrierRateCards ?? [])
            .Select(carrierRateCard => ToCarrierRateCard(carrierRateCard, options.Value.Path))
            .ToDictionary(carrierRateCard => carrierRateCard.CarrierId);
    }

    public CarrierRateCard? FindFor(CarrierId carrierId) => _carrierRateCards.GetValueOrDefault(carrierId);

    private static CarrierRateCard ToCarrierRateCard(SeedCarrierRateCard carrierRateCard, string path) => new(
        new RateCardId(carrierRateCard.Id),
        new CarrierId(carrierRateCard.CarrierId),
        [.. carrierRateCard.Versions.Select(version => SeedRateCardVersionMapper.ToVersion(version, RateCardKind.CarrierRateCard, carrierRateCard.Id, path))]);

    private sealed record SeedFile(IReadOnlyList<SeedCarrierRateCard>? CarrierRateCards);

    private sealed record SeedCarrierRateCard(string Id, string CarrierId, IReadOnlyList<SeedRateCardVersion> Versions);
}
