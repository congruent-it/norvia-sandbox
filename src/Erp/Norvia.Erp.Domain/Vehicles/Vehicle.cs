using Ardalis.SharedKernel;

namespace Norvia.Erp.Domain.Vehicles;

/// <summary>
/// A Vehicle of the Own fleet, as the ERP knows it. The ERP owns Vehicle master data (ADR 0014); the TMS owns its availability. It starts from
/// the seed file with the TMS's IDs, at Master-data revision 1, and Finance edits it from there; each edit raises the revision and is replicated
/// to the TMS (E3). Making a Vehicle inactive stores only <c>Active = false</c>: it edits no Roster and opens or closes no day.
/// </summary>
public sealed class Vehicle : EntityBase<VehicleId>, IAggregateRoot
{
    private Vehicle()
    {
    }

    public Vehicle(VehicleId id, string registration, VehicleType type, int payloadKg, int palletPlaces, bool active)
    {
        Check(registration, payloadKg, palletPlaces);
        Id = id;
        Registration = registration;
        Type = type;
        PayloadKg = payloadKg;
        PalletPlaces = palletPlaces;
        Active = active;
    }

    public string Registration { get; private set; } = string.Empty;

    public VehicleType Type { get; private set; }

    public int PayloadKg { get; private set; }

    public int PalletPlaces { get; private set; }

    public bool Active { get; private set; }

    /// <summary>The Master-data revision: 1 as loaded from the seed file, raised by every edit.</summary>
    public int Revision { get; private set; } = 1;

    /// <summary>The sender's Idempotency-Key of the save that last changed the record, so that save sent again is a repeat; null until a save with a key did.</summary>
    public string? LastSaveKey { get; private set; }

    /// <summary>True when <paramref name="key"/> is the key of the save that last changed the record.</summary>
    public bool IsRepeat(string? key) => key is not null && key == LastSaveKey;

    /// <summary>True once Finance created or edited the Vehicle, so the seed file no longer speaks for it.</summary>
    public bool FinanceOwned { get; private set; }

    /// <summary>A Vehicle Finance creates: revision 1, and the seed file never reloads it.</summary>
    public static Vehicle CreateByFinance(VehicleId id, string registration, VehicleType type, int payloadKg, int palletPlaces, bool active, string? saveKey = null) =>
        new(id, registration, type, payloadKg, palletPlaces, active) { FinanceOwned = true, LastSaveKey = saveKey };

    /// <summary>Takes over what the seed file says now, silently, and only while Finance has not created or edited the Vehicle.</summary>
    public void Reload(string registration, VehicleType type, int payloadKg, int palletPlaces, bool active)
    {
        Check(registration, payloadKg, palletPlaces);
        if (FinanceOwned)
        {
            return;
        }

        Set(registration, type, payloadKg, palletPlaces, active);
    }

    /// <summary>
    /// Finance's edit: replaces the fields and raises the Master-data revision. A repeat of what the record already is changes nothing,
    /// raises no revision and leaves the seed file in charge; it answers false so nothing is published.
    /// </summary>
    /// <returns>True when the edit changed the record.</returns>
    public bool Edit(string registration, VehicleType type, int payloadKg, int palletPlaces, bool active, string? saveKey = null)
    {
        Check(registration, payloadKg, palletPlaces);
        if (Registration == registration && Type == type && PayloadKg == payloadKg && PalletPlaces == palletPlaces && Active == active)
        {
            return false;
        }

        Set(registration, type, payloadKg, palletPlaces, active);
        FinanceOwned = true;
        LastSaveKey = saveKey;
        Revision++;
        return true;
    }

    private static void Check(string registration, int payloadKg, int palletPlaces)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(payloadKg);
        ArgumentOutOfRangeException.ThrowIfNegative(palletPlaces);
    }

    private void Set(string registration, VehicleType type, int payloadKg, int palletPlaces, bool active)
    {
        Registration = registration;
        Type = type;
        PayloadKg = payloadKg;
        PalletPlaces = palletPlaces;
        Active = active;
    }
}
