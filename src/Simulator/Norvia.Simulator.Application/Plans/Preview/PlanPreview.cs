using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Plans.Preview;

/// <summary>What a run would send, per Shipper and booking, made from the same seed the run would use.</summary>
public sealed record PlanPreview(
    int RunNumber,
    SimulationSettings Settings,
    IReadOnlyList<ShipperShare> PerShipper,
    IReadOnlyList<PlanRow> Rows)
{
    public int BadBookings => Rows.Count(row => row.Bad is not null);

    internal static PlanPreview From(SimulationPlan plan) => new(
        plan.RunNumber,
        plan.Settings,
        plan.PerShipper(),
        [.. plan.Bookings.Select(booking => new PlanRow(
            booking.Step, booking.PersonaName, booking.ShipperName, booking.Reference,
            $"{booking.PickupZone} → {booking.DeliveryZone}", booking.Slot.DayOffset, booking.TotalWeightKg, booking.Bad, booking.Expected))]);
}
