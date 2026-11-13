using Ardalis.SharedKernel;

namespace Norvia.Erp.Domain.Shippers;

/// <summary>
/// A Shipper, as the ERP knows it. The ERP owns Shipper master data (ADR 0002). It starts from the seed file with the TMS's IDs, at
/// Master-data revision 1, and Finance edits it from there; each edit raises the revision and is replicated to the TMS (E3).
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

    /// <summary>The Master-data revision: 1 as loaded from the seed file, raised by every edit.</summary>
    public int Revision { get; private set; } = 1;

    /// <summary>True once Finance created or edited the Shipper, so the seed file no longer speaks for it.</summary>
    public bool FinanceOwned { get; private set; }

    /// <summary>A Shipper Finance creates: revision 1, and the seed file never reloads it.</summary>
    public static Shipper CreateByFinance(ShipperId id, string name, bool active) =>
        new(id, name, active) { FinanceOwned = true };

    /// <summary>Takes over what the seed file says now, silently, and only while Finance has not created or edited the Shipper.</summary>
    public void Reload(string name, bool active)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (FinanceOwned)
        {
            return;
        }

        Name = name;
        Active = active;
    }

    /// <summary>Finance's edit: replaces the fields and raises the Master-data revision.</summary>
    public void Edit(string name, bool active)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Active = active;
        FinanceOwned = true;
        Revision++;
    }
}
