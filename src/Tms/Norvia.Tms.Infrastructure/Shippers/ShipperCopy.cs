namespace Norvia.Tms.Infrastructure.Shippers;

/// <summary>A Shipper the ERP replicated to the TMS, kept whole. It overrides the seed file's Shipper with the same ID (E3).</summary>
internal sealed class ShipperCopy
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; }

    public int Revision { get; set; }
}
