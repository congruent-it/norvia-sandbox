namespace Norvia.Tms.Infrastructure.RateCards;

/// <summary>A Rate Card the ERP replicated to the TMS, kept whole with its list of versions as JSON. It overrides the seed file's Rate Card with the same ID (E3).</summary>
internal sealed class RateCardCopy
{
    public string Id { get; set; } = string.Empty;

    public string ShipperId { get; set; } = string.Empty;

    public int Revision { get; set; }

    public string VersionsJson { get; set; } = "[]";
}
