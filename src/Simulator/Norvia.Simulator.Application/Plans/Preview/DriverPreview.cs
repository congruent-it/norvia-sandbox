using Norvia.Simulator.Domain.Drivers;

namespace Norvia.Simulator.Application.Plans.Preview;

/// <summary>
/// The Driver stage of the funnel, for the Shipments the Planner means to put on the Own fleet and that collect today (<see cref="Shipments"/>). It is the most the
/// stage can reach: an order meant for Own fleet goes to a Carrier when the day is full, and what the TMS refuses or finds no Vehicle and Driver for is not known
/// until the run. The shares are the same ones the run applies to the Shipments that really run today.
/// </summary>
public sealed record DriverPreview(DriverShares Shares, DriverFunnelCounts Counts)
{
    public int Shipments => Counts.Shipments;
}
