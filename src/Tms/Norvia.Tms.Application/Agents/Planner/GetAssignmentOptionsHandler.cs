using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>
/// Works out the assignment options of a Transport Order from the code P3, P10 and P9 already run, not a second copy of their rules: the
/// Assignment rules (<see cref="AssignmentRules.Check"/>), the load check with the order counted (<see cref="LoadCheckWarnings"/>), the
/// Driving-hour budget (<see cref="DrivingHourBudget"/>) and the Carrier Rate Card's pricing (<see cref="AssignmentRules.PriceCarrierBooking"/>).
/// It reads only; assigning stays the Planner's own command.
/// </summary>
public sealed class GetAssignmentOptionsHandler(
    IReadRepository<TransportOrder> transportOrders,
    IReadRepository<PlanningDay> days,
    IShipperDirectory shippers,
    ICarrierDirectory carriers,
    ICarrierRateCardDirectory carrierRateCards,
    IVehicleDirectory vehicles,
    IDriverDirectory drivers,
    IPlannedDrivingHours plannedDriving,
    DrivingHourLimits limits,
    LoadCheckWarnings loadChecks,
    TimeProvider time)
    : IQueryHandler<GetAssignmentOptionsQuery, Result<AssignmentOptions>>
{
    public async ValueTask<Result<AssignmentOptions>> Handle(GetAssignmentOptionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TransportOrderNumber.TryParse(query.TransportOrderNumber, out var number)
            || await transportOrders.FirstOrDefaultAsync(new TransportOrderByNumberSpec(number), cancellationToken) is not { } order
            || order.Number is null)
        {
            return Result.NotFound();
        }

        var details = order.Details;
        var demand = LoadCheck.DemandOf(details);
        return new AssignmentOptions(
            order.Number.Value,
            order.Status,
            shippers.Find(order.ShipperId)?.Name,
            order.RatingResult,
            PlaceView.Of(details.Collection?.Address),
            PlaceView.Of(details.Delivery?.Address),
            details.Collection?.Window?.Earliest,
            details.Collection?.Window?.Latest,
            details.Delivery?.Window?.Earliest,
            details.Delivery?.Window?.Latest,
            demand.WeightKg,
            demand.PalletPlaces,
            await OwnFleetAsync(order, cancellationToken),
            CarriersFor(order));
    }

    private async Task<OwnFleetOption> OwnFleetAsync(TransportOrder order, CancellationToken cancellationToken)
    {
        var refusal = AssignmentRules.Check(order, carrierId: null, carrier: null);
        var check = await loadChecks.IfAssignedAsync(order, cancellationToken);
        var day = CollectionDate.Of(order.Details);
        if (day is null)
        {
            return new OwnFleetOption(refusal, OwnFleetFit.NoCollectionDay, null, null, null, 0, 0, 0, null);
        }

        if (check is { OutsideHorizon: true })
        {
            return new OwnFleetOption(refusal, OwnFleetFit.OutsideHorizon, day, check, null, 0, 0, 0, null);
        }

        var planningDay = await days.FirstOrDefaultAsync(new PlanningDayByDateSpec(day.Value), cancellationToken);
        if (check is null || planningDay is null)
        {
            return new OwnFleetOption(refusal, OwnFleetFit.DayNotOpen, day, null, null, 0, 0, 0, null);
        }

        var activeVehicles = vehicles.ListActive().Select(vehicle => vehicle.Id).ToHashSet();
        var activeDrivers = drivers.ListActive().Select(driver => driver.Id).ToHashSet();
        var availableVehicles = planningDay.AvailableVehicles.Count(activeVehicles.Contains);
        var working = planningDay.Shifts.Select(shift => shift.DriverId).Where(activeDrivers.Contains).Distinct().ToList();

        var (first, last) = DrivingHourBudget.Window(day.Value);
        var driving = await plannedDriving.ListAsync(first, last, cancellationToken);
        var left = working
            .Select(driver => DrivingHourBudget.Left(driving.Where(planned => planned.DriverId == driver), day.Value, limits))
            .ToList();
        var withHours = left.Where(hours => hours.Today > TimeSpan.Zero && hours.Week > TimeSpan.Zero && hours.Fortnight > TimeSpan.Zero).ToList();

        // Nobody to drive it outranks a capacity warning: Over capacity never blocks, but Own fleet with no Driver cannot carry the order at all.
        var fit = withHours.Count == 0 ? OwnFleetFit.NoDriverWithDrivingHoursLeft
            : check.Load is { Over: true } ? OwnFleetFit.OverCapacity
            : OwnFleetFit.Fits;
        return new OwnFleetOption(
            refusal,
            fit,
            day,
            check is { Load.Over: true } ? check : null,
            check.Load,
            availableVehicles,
            working.Count,
            withHours.Count,
            left.Count == 0 ? null : left.Max(hours => hours.Today));
    }

    private IReadOnlyList<CarrierOption> CarriersFor(TransportOrder order)
    {
        var now = time.GetUtcNow();
        return
        [
            .. carriers.ListActive().Select(carrier =>
            {
                var refusal = AssignmentRules.Check(order, carrier.Id, carrier);
                var (cost, notPriced) = AssignmentRules.PriceCarrierBooking(carrier, carrierRateCards.FindFor(carrier.Id), order.Details, now);
                return new CarrierOption(carrier.Id.Value, carrier.Name, refusal, cost?.Total.Amount, notPriced?.Code);
            }),
        ];
    }
}
