using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Invoicing;

namespace Norvia.Simulator.Domain.Runs;

/// <summary>A named set of numbers that fills the Configure screen, such as "Quiet Monday".</summary>
public sealed record Preset(
    string Name,
    string Description,
    int OrderCount,
    int BadSharePercent,
    int CollectedTodayPercent,
    RunMode Mode,
    TimeSpan Window,
    int AssignedPercent = SimulationSettings.DefaultAssignedPercent,
    int OwnFleetPercent = SimulationSettings.DefaultOwnFleetPercent,
    int ReassignPercent = SimulationSettings.DefaultReassignPercent,
    CarrierShares? Carriers = null,
    DriverShares? Drivers = null,
    InvoicingShares? Invoicing = null)
{
    /// <summary>The settings this Preset fills in, with the seed the presenter chose.</summary>
    public SimulationSettings ToSettings(int seed) =>
        new(OrderCount, BadSharePercent, CollectedTodayPercent, seed, Mode, Window, Name, AssignedPercent, OwnFleetPercent, ReassignPercent)
        {
            Carriers = Carriers ?? new CarrierShares(),
            Drivers = Drivers ?? new DriverShares(),
            Invoicing = Invoicing ?? new InvoicingShares(),
        };
}
