using System.Text.Json;
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
    private static readonly JsonSerializerOptions SeedJson = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<ShipperId, RateCard> _rateCards;

    public SeedFileRateCardDirectory(IOptions<SeedOptions> options)
    {
        using var file = File.OpenRead(options.Value.Path);
        var seed = JsonSerializer.Deserialize<SeedFile>(file, SeedJson)
            ?? throw new InvalidDataException($"The seed file '{options.Value.Path}' is empty.");

        _rateCards = (seed.RateCards ?? [])
            .Select(rateCard => ToRateCard(rateCard, options.Value.Path))
            .ToDictionary(rateCard => rateCard.ShipperId);
    }

    public RateCard? FindFor(ShipperId shipperId) => _rateCards.GetValueOrDefault(shipperId);

    private static RateCard ToRateCard(SeedRateCard rateCard, string path) => new(
        new RateCardId(rateCard.Id),
        new ShipperId(rateCard.ShipperId),
        [.. rateCard.Versions.Select(version => SeedRateCardVersions.ToVersion(version, "Rate Card", rateCard.Id, path))]);

    private sealed record SeedFile(IReadOnlyList<SeedRateCard>? RateCards);

    private sealed record SeedRateCard(string Id, string ShipperId, IReadOnlyList<SeedRateCardVersion> Versions);
}
