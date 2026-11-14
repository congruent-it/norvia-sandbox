using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>A Vehicle the ERP replicated to the TMS, kept whole. It overrides the seed file's Vehicle with the same ID (E3).</summary>
public sealed class VehicleCopy : IAggregateRoot
{
    public string Id { get; set; } = string.Empty;

    public string Registration { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public int PayloadKg { get; set; }

    public int PalletPlaces { get; set; }

    public bool Active { get; set; }

    public int Revision { get; set; }
}
