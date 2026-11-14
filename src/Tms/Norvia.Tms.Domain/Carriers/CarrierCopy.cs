using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.Carriers;

/// <summary>A Carrier the ERP replicated to the TMS, kept whole. It overrides the seed file's Carrier with the same ID (E3).</summary>
public sealed class CarrierCopy : IAggregateRoot
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; }

    public int Revision { get; set; }
}
