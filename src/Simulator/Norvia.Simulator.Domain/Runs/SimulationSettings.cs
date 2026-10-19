using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Invoicing;

namespace Norvia.Simulator.Domain.Runs;

/// <summary>What the presenter configures for a run. The same settings and the same seed always give the same Simulation plan.</summary>
public sealed record SimulationSettings(
    int OrderCount,
    int BadSharePercent,
    int CollectedTodayPercent,
    int Seed,
    RunMode Mode,
    TimeSpan Window,
    string? PresetName = null,
    int AssignedPercent = SimulationSettings.DefaultAssignedPercent,
    int OwnFleetPercent = SimulationSettings.DefaultOwnFleetPercent,
    int ReassignPercent = SimulationSettings.DefaultReassignPercent)
{
    /// <summary>The Carrier stage of the funnel: what becomes of the Shipments assigned to a Carrier. A plan made before it existed has the defaults.</summary>
    public CarrierShares Carriers { get; init; } = new();

    /// <summary>The Driver stage of the funnel: what goes wrong on the road for the Shipments on today's Trips. A plan made before it existed has the defaults.</summary>
    public DriverShares Drivers { get; init; } = new();

    /// <summary>The invoicing stage of the funnel: what Finance does once the deliveries are in. A plan made before it existed has the defaults.</summary>
    public InvoicingShares Invoicing { get; init; } = new();

    /// <summary>The share of the Shipments that stay with the Own fleet that the Planner puts on Trips; the rest stay Planned. A plan made before the Trips existed has the default.</summary>
    public int TripPercent { get; init; } = DefaultTripPercent;

    public const int DefaultTripPercent = 100;
    public const int DefaultAssignedPercent = 80;
    public const int DefaultOwnFleetPercent = 50;
    public const int DefaultReassignPercent = 5;

    public const int MaxWindowMinutes = 240;

    /// <summary>How long apart <paramref name="bookingCount"/> bookings go out: none in a burst, evenly over the window when paced.</summary>
    public TimeSpan Spacing(int bookingCount) =>
        Mode == RunMode.Paced && bookingCount > 0 ? Window / bookingCount : TimeSpan.Zero;

    /// <summary>Everything wrong with these settings or the limits they break; empty when the run may go ahead.</summary>
    public IReadOnlyList<string> Problems(SimulationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        var problems = new List<string>();
        if (OrderCount < 1)
        {
            problems.Add("A run needs at least one order.");
        }
        else if (OrderCount > limits.MaxOrdersPerRun)
        {
            problems.Add($"A run may hold at most {limits.MaxOrdersPerRun} orders; this one has {OrderCount}.");
        }

        if (BadSharePercent is < 0 or > 100)
        {
            problems.Add("The share of bad bookings is a percentage from 0 to 100.");
        }

        if (CollectedTodayPercent is < 0 or > 100)
        {
            problems.Add("The share of orders collected today is a percentage from 0 to 100.");
        }

        if (AssignedPercent is < 0 or > 100)
        {
            problems.Add("The share of orders the Planner assigns is a percentage from 0 to 100.");
        }

        if (OwnFleetPercent is < 0 or > 100)
        {
            problems.Add("The share of assignments to Own fleet is a percentage from 0 to 100.");
        }

        if (ReassignPercent is < 0 or > 100)
        {
            problems.Add("The share of assignments the Planner reassigns is a percentage from 0 to 100.");
        }

        if (TripPercent is < 0 or > 100)
        {
            problems.Add("The share of Own fleet Shipments the Planner puts on Trips is a percentage from 0 to 100.");
        }

        if (Mode == RunMode.Paced && (Window <= TimeSpan.Zero || Window > TimeSpan.FromMinutes(MaxWindowMinutes)))
        {
            problems.Add($"A paced run is spread over 1 to {MaxWindowMinutes} minutes.");
        }

        problems.AddRange(Carriers.Problems());
        problems.AddRange(Drivers.Problems());
        problems.AddRange(Invoicing.Problems());
        return problems;
    }
}
