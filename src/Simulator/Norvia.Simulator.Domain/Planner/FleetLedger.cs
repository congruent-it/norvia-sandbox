namespace Norvia.Simulator.Domain.Planner;

/// <summary>
/// The room the Own fleet has on each open day, as the Planner sees it: what the TMS reported when the Planner began, less what
/// the Planner has put on it since. A day the TMS didn't report is closed or not yet open, and has no room.
/// </summary>
public sealed class FleetLedger(IEnumerable<FleetDay> days)
{
    private readonly Dictionary<DateOnly, (decimal Kg, int PalletPlaces)> _room = days
        .ToDictionary(day => day.Date, day => (day.CapacityKg - day.DemandKg, day.CapacityPalletPlaces - day.DemandPalletPlaces));

    /// <summary>Takes the room for an order on a day, and says whether there was any. Nothing is taken when it doesn't fit.</summary>
    public bool TryReserve(DateOnly day, decimal weightKg, int palletPlaces)
    {
        if (!_room.TryGetValue(day, out var room) || weightKg > room.Kg || palletPlaces > room.PalletPlaces)
        {
            return false;
        }

        _room[day] = (room.Kg - weightKg, room.PalletPlaces - palletPlaces);
        return true;
    }

    /// <summary>Gives the room back, when the order wasn't assigned after all or moved to a Carrier.</summary>
    public void Release(DateOnly day, decimal weightKg, int palletPlaces)
    {
        if (_room.TryGetValue(day, out var room))
        {
            _room[day] = (room.Kg + weightKg, room.PalletPlaces + palletPlaces);
        }
    }
}
