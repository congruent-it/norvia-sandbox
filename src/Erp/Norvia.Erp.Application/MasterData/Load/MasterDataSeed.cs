using Norvia.Erp.Domain.RateCards;

namespace Norvia.Erp.Application.MasterData.Load;

/// <summary>The master data the seed file holds, with the IDs the TMS uses.</summary>
public sealed record MasterDataSeed(
    IReadOnlyList<SeedShipper> Shippers,
    IReadOnlyList<SeedCarrier> Carriers,
    IReadOnlyList<SeedRateCard> RateCards,
    IReadOnlyList<SeedRateCard> CarrierRateCards);

public sealed record SeedShipper(string Id, string Name, bool Active);

public sealed record SeedCarrier(string Id, string Name, bool Active);

/// <param name="PartyId">The Shipper's ID on a Rate Card, the Carrier's on a Carrier Rate Card.</param>
public sealed record SeedRateCard(string Id, string PartyId, IReadOnlyList<RateCardVersion> Versions);
