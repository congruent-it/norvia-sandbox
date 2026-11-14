namespace Norvia.Tms.Infrastructure.RateCards;

/// <summary>A Carrier Rate Card the ERP replicated to the TMS, kept whole with its list of versions as JSON. It overrides the seed file's card with the same ID (E3).</summary>
internal sealed class CarrierRateCardCopy
{
    public string Id { get; set; } = string.Empty;

    public string CarrierId { get; set; } = string.Empty;

    public int Revision { get; set; }

    public string VersionsJson { get; set; } = "[]";
}
