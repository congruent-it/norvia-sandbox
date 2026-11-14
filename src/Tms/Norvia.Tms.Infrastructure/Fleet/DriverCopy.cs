namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>A Driver the ERP replicated to the TMS, kept whole. It overrides the seed file's Driver with the same ID (E3).</summary>
internal sealed class DriverCopy
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>The licence categories as one comma-separated text, such as <c>B,C,CE</c>; empty for a Driver without a licence.</summary>
    public string Licences { get; set; } = string.Empty;

    public bool Active { get; set; }

    public int Revision { get; set; }
}
