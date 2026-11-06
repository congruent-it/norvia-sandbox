using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Invoicing;
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
    PlannerPreview Planner,
    CarrierPreview Carriers,
    DriverPreview Drivers,
    InvoicingPreview Invoicing)
{
    /// <summary>The pools of Carrier texts the run draws from, with a sample of each.</summary>
    public IReadOnlyList<PoolPreview> Pools { get; init; } = [];

    /// <summary>What to tell the operator about the pools, such as a model that failed and a built-in pool used instead.</summary>
    public IReadOnlyList<string> PoolNotes { get; init; } = [];

    public int BadBookings => Rows.Count(row => row.Bad is not null);

    internal static PlanPreview From(SimulationPlan plan)
    {
        var choices = plan.Choices.ToDictionary(choice => choice.Step);
        var preview = new PlanPreview(
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
                plan.Choices.Count(choice => choice.Reassign)),
            new CarrierPreview(
                plan.Settings.Carriers,
                CarrierFunnel.Count(CarrierFunnel.Plan(plan.Settings.Carriers, plan.Settings.Seed, plan.Choices.Count(choice => choice.Intent == PlannerIntent.Carrier)))),
            new DriverPreview(
                plan.Settings.Drivers,
                DriverFunnel.Count(DriverFunnel.Plan(
                    plan.Settings.Drivers, plan.Settings.Seed,
                    plan.Bookings.Count(booking => booking.Slot.DayOffset == 0 && choices.GetValueOrDefault(booking.Step)?.Intent == PlannerIntent.OwnFleetIfItFits)
                        * plan.Settings.TripPercent / 100))),
            new InvoicingPreview(
                plan.Settings.Invoicing,
                InvoicingFunnel.Count(InvoicingFunnel.Plan(
                    plan.Settings.Invoicing, plan.Settings.Seed,
                    plan.Bookings.Where(booking => booking.Expected == ExpectedAnswer.Accepted).Select(booking => booking.ShipperId).Distinct().Count()))));

        return preview with { Pools = PoolsOf(plan), PoolNotes = plan.PoolNotes };
    }

    private const int SampleSize = 3;

    private static List<PoolPreview> PoolsOf(SimulationPlan plan) =>
    [
        .. plan.Pools.Select(pool => new PoolPreview(
            pool.ProfileId, pool.Category.ToString(), pool.Language, pool.Source, pool.Texts.Count, [.. pool.Texts.Take(SampleSize)],
            [.. plan.CarrierProfiles.Where(pair => pair.Value == pool.ProfileId).Select(pair => pair.Key).Order(StringComparer.Ordinal)])),
    ];
}
