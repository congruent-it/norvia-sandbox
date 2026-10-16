namespace Norvia.Simulator.Domain.Planner;

/// <summary>One open day of the Own fleet's Planning horizon as the TMS reports it (P10): what the fleet can carry and what is planned for it, and which Drivers work.</summary>
public sealed record FleetDay(DateOnly Date, decimal CapacityKg, int CapacityPalletPlaces, decimal DemandKg, int DemandPalletPlaces)
{
    /// <summary>The Drivers who work the day, with their Shifts.</summary>
    public IReadOnlyList<DriverShift> Shifts { get; init; } = [];
}
