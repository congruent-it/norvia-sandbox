using Ardalis.SharedKernel;

namespace Norvia.Erp.Domain.Shippers;

/// <summary>
/// A Shipper, as the ERP knows it. The ERP owns Shipper master data (ADR 0002); until editing it is built (E3) it comes from the
/// seed file with the TMS's IDs, and the ERP's UI only shows it.
/// </summary>
public sealed class Shipper : EntityBase<ShipperId>, IAggregateRoot
{
    private Shipper()
    {
    }

    public Shipper(ShipperId id, string name, bool active)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
        Active = active;
    }

    public string Name { get; private set; } = string.Empty;

    public bool Active { get; private set; }

    /// <summary>Takes over what the seed file says now.</summary>
    public void Reload(string name, bool active)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Active = active;
    }
}
