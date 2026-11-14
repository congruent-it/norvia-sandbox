using Ardalis.SharedKernel;

namespace Norvia.Erp.Domain.Drivers;

/// <summary>
/// A Driver of the Own fleet, as the ERP knows it. The ERP owns Driver master data (ADR 0014); the TMS owns Rosters, Shifts and Absences. It
/// starts from the seed file with the TMS's IDs, at Master-data revision 1, and Finance edits it from there; each edit raises the revision and
/// is replicated to the TMS (E3). Making a Driver inactive stores only <c>Active = false</c>: it edits no Roster and opens or closes no day.
/// </summary>
public sealed class Driver : EntityBase<DriverId>, IAggregateRoot
{
    private Driver()
    {
    }

    public Driver(DriverId id, string name, IEnumerable<LicenceCategory> licences, bool active)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(licences);
        Id = id;
        Name = name;
        Licences = Normalise(licences);
        Active = active;
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>The licence categories held, each once, lowest first.</summary>
    public IReadOnlyList<LicenceCategory> Licences { get; private set; } = [];

    public bool Active { get; private set; }

    /// <summary>The Master-data revision: 1 as loaded from the seed file, raised by every edit.</summary>
    public int Revision { get; private set; } = 1;

    /// <summary>The sender's Idempotency-Key of the save that last changed the record, so that save sent again is a repeat; null until a save with a key did.</summary>
    public string? LastSaveKey { get; private set; }

    /// <summary>True when <paramref name="key"/> is the key of the save that last changed the record.</summary>
    public bool IsRepeat(string? key) => key is not null && key == LastSaveKey;

    /// <summary>True once Finance created or edited the Driver, so the seed file no longer speaks for it.</summary>
    public bool FinanceOwned { get; private set; }

    /// <summary>A Driver Finance creates: revision 1, and the seed file never reloads it.</summary>
    public static Driver CreateByFinance(DriverId id, string name, IEnumerable<LicenceCategory> licences, bool active, string? saveKey = null) =>
        new(id, name, licences, active) { FinanceOwned = true, LastSaveKey = saveKey };

    /// <summary>Takes over what the seed file says now, silently, and only while Finance has not created or edited the Driver.</summary>
    public void Reload(string name, IEnumerable<LicenceCategory> licences, bool active)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(licences);
        if (FinanceOwned)
        {
            return;
        }

        Name = name;
        Licences = Normalise(licences);
        Active = active;
    }

    /// <summary>
    /// Finance's edit: replaces the fields and raises the Master-data revision. A repeat of what the record already is, the licences compared
    /// whatever their order, changes nothing, raises no revision and leaves the seed file in charge; it answers false so nothing is published.
    /// </summary>
    /// <returns>True when the edit changed the record.</returns>
    public bool Edit(string name, IEnumerable<LicenceCategory> licences, bool active, string? saveKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(licences);
        var normalised = Normalise(licences);
        if (Name == name && Active == active && Licences.SequenceEqual(normalised))
        {
            return false;
        }

        Name = name;
        Licences = normalised;
        Active = active;
        FinanceOwned = true;
        LastSaveKey = saveKey;
        Revision++;
        return true;
    }

    private static List<LicenceCategory> Normalise(IEnumerable<LicenceCategory> licences) => [.. licences.Distinct().Order()];
}
