namespace Norvia.Simulator.Domain.Planner;

/// <summary>What an order asks of the Own fleet, or a day has room for: weight and pallet places, as the TMS's load check counts them (P10).</summary>
public sealed record FleetLoad(decimal WeightKg, int PalletPlaces);
