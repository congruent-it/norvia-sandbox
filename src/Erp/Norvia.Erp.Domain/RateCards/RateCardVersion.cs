namespace Norvia.Erp.Domain.RateCards;

/// <summary>One version of a Rate Card or a Carrier Rate Card, with the Zones and Lanes it prices. The ERP keeps them as the seed file has them; the TMS rates with its own copy.</summary>
/// <param name="ValidFrom">The first day the version is valid.</param>
/// <param name="ValidTo">The last day the version is valid, or null when it has no end yet.</param>
public sealed record RateCardVersion(int Version, DateOnly ValidFrom, DateOnly? ValidTo, IReadOnlyList<Zone> Zones, IReadOnlyList<Lane> Lanes);
