namespace Norvia.Tms.Application.Fleet.Planning;

/// <summary>A day's load check (P10): what the Own fleet can carry against the Own fleet work planned for it.</summary>
/// <param name="Over">Over capacity: the demand is more than the capacity in weight or in pallet places.</param>
public sealed record DayLoadView(decimal CapacityKg, int CapacityPalletPlaces, decimal DemandKg, int DemandPalletPlaces, bool Over);
