namespace Norvia.Tms.Domain.Fleet;

/// <summary>What the Own fleet can carry on a day, or has been given to carry: weight in kg and pallet places.</summary>
public sealed record FleetLoad(decimal WeightKg, int PalletPlaces)
{
    public static readonly FleetLoad None = new(0m, 0);

    /// <summary>True when this load is more than the capacity in weight or in pallet places.</summary>
    public bool Exceeds(FleetLoad capacity)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        return WeightKg > capacity.WeightKg || PalletPlaces > capacity.PalletPlaces;
    }
}
