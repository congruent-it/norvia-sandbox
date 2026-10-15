using Norvia.Simulator.Domain.Carriers;

namespace Norvia.Simulator.Application.Plans.Preview;

/// <summary>
/// The Carrier stage of the funnel, for the Shipments the Planner means to put on a Carrier (<see cref="Shipments"/>). It is the most the
/// stage can reach: an order meant for Own fleet where it fits goes to a Carrier only when the day is full, and what the TMS refuses
/// is not known until the run. The shares are the same ones the run applies to the Shipments that really go to a Carrier.
/// </summary>
public sealed record CarrierPreview(CarrierShares Shares, CarrierFunnelCounts Counts)
{
    public int Shipments => Counts.Shipments;
}
