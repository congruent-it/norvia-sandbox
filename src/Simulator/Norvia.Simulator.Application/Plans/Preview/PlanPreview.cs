using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Plans.Preview;

/// <summary>What a run would send, per Shipper and booking, made from the same seed the run would use.</summary>
public sealed record PlanPreview(
    int RunNumber,
    SimulationSettings Settings,
    IReadOnlyList<ShipperShare> PerShipper,
    IReadOnlyList<PlanRow> Rows,
    PlannerPreview Planner)
{
    public int BadBookings => Rows.Count(row => row.Bad is not null);

    internal static PlanPreview From(SimulationPlan plan)
    {
        var choices = plan.Choices.ToDictionary(choice => choice.Step);
        return new PlanPreview(
            plan.RunNumber,
            plan.Settings,
            plan.PerShipper(),
            [.. plan.Bookings.Select(booking =>
            {
                var choice = choices.GetValueOrDefault(booking.Step);
                return new PlanRow(
                    booking.Step, booking.PersonaName, booking.ShipperName, booking.Reference,
                    $"{booking.PickupZone} → {booking.DeliveryZone}", booking.Slot.DayOffset, booking.TotalWeightKg, booking.Bad, booking.Expected,
                    choice?.Intent, choice?.Reassign ?? false);
            })],
            new PlannerPreview(
                plan.Bookings.Count(booking => booking.Expected == ExpectedAnswer.Accepted),
                plan.Choices.Count,
                plan.Choices.Count(choice => choice.Intent == PlannerIntent.OwnFleetIfItFits),
                plan.Choices.Count(choice => choice.Intent == PlannerIntent.Carrier),
                plan.Choices.Count(choice => choice.Reassign)));
    }
}
