namespace Norvia.Tms.Domain.RateCards;

/// <summary>Names one version of a Rate Card, as a Freight Charge records the version it was calculated from.</summary>
public sealed record RateCardVersionReference(string RateCardId, int Version);
