using Ardalis.SharedKernel;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Fleet.Planning;

/// <summary>Works out the load check warning for a Shipment just assigned to Own fleet (P10), with the Shipment itself counted.</summary>
public sealed partial class LoadCheckWarnings(
    IReadRepository<PlanningDay> days,
    IReadRepository<TransportOrder> transportOrders,
    DayLoads loads,
    PlanningHorizonRules horizon,
    TimeProvider time,
    ILogger<LoadCheckWarnings> logger)
{
    /// <summary>
    /// The warning for the Shipment's collection day, or null when the Shipment isn't carried by Own fleet, has no collection
    /// day, or the day is within the horizon and not Over capacity. Call it once the Shipment is saved, so the demand holds it.
    /// A failure to work it out is logged and gives no warning: the assignment is already saved, and capacity never blocks it.
    /// </summary>
    public async Task<LoadCheckWarning?> ForAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        if (shipment.CarriedBy.CarrierId is not null)
        {
            return null;
        }

        try
        {
            return await WarningForAsync(shipment, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogWarningFailed(exception, shipment.Number.Value);
            return null;
        }
    }

    /// <summary>
    /// The load check for the collection day of a Transport Order that is not planned yet, with the order counted as if it were
    /// assigned to Own fleet now (P10). Null when the order has no collection day, or the day is within the horizon but not open.
    /// Unlike <see cref="ForAsync"/>, a failure to work it out is not swallowed: nothing is saved that it could be late for.
    /// It also counts <paramref name="alreadyAssumed"/>, the what-if tally (#445): Transport Orders a plan assumes are on Own fleet already, though none is
    /// assigned. An assumed order that collects on another day adds nothing to this order's day.
    /// </summary>
    public async Task<LoadCheckWarning?> IfAssignedAsync(TransportOrder order, IReadOnlyList<TransportOrder> alreadyAssumed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(alreadyAssumed);
        return await LoadCheckAsync(order, countOrder: true, alreadyAssumed, cancellationToken);
    }

    private async Task<LoadCheckWarning?> WarningForAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        var orders = await transportOrders.ListAsync(new TransportOrdersByIdsSpec([shipment.TransportOrderId]), cancellationToken);
        if (orders.FirstOrDefault() is not { } order)
        {
            return null;
        }

        var check = await LoadCheckAsync(order, countOrder: false, [], cancellationToken);
        return check is { OutsideHorizon: true } or { Load.Over: true } ? check : null;
    }

    private async Task<LoadCheckWarning?> LoadCheckAsync(TransportOrder order, bool countOrder, IReadOnlyList<TransportOrder> alreadyAssumed, CancellationToken cancellationToken)
    {
        if (CollectionDate.Of(order.Details) is not { } date)
        {
            return null;
        }

        if (!horizon.DaysFrom(horizon.Today(time.GetUtcNow())).Contains(date))
        {
            return new LoadCheckWarning(date, OutsideHorizon: true, Load: null);
        }

        if (await days.FirstOrDefaultAsync(new PlanningDayByDateSpec(date), cancellationToken) is not { } day)
        {
            return null;
        }

        var demand = await loads.DemandByDayAsync(cancellationToken);
        if (countOrder)
        {
            demand = new Dictionary<DateOnly, FleetLoad>(demand)
            {
                [date] = LoadCheck.Sum(
                [
                    demand.GetValueOrDefault(date, FleetLoad.None),
                    LoadCheck.DemandOf(order.Details),
                    .. alreadyAssumed.Where(assumed => CollectionDate.Of(assumed.Details) == date).Select(assumed => LoadCheck.DemandOf(assumed.Details)),
                ]),
            };
        }

        return new LoadCheckWarning(date, OutsideHorizon: false, loads.Of(day, demand));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The load check warning for Shipment {ShipmentNumber} couldn't be worked out")]
    private partial void LogWarningFailed(Exception exception, string shipmentNumber);
}
