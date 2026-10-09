using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Fleet.Planning;

/// <summary>Works out the load check warning for a Shipment just assigned to Own fleet (P10), with the Shipment itself counted.</summary>
public sealed class LoadCheckWarnings(
    IReadRepository<PlanningDay> days,
    IReadRepository<TransportOrder> transportOrders,
    DayLoads loads,
    PlanningHorizonRules horizon,
    TimeProvider time)
{
    /// <summary>
    /// The warning for the Shipment's collection day, or null when the Shipment isn't carried by Own fleet, has no collection
    /// day, or the day is within the horizon and not Over capacity. Call it once the Shipment is saved, so the demand holds it.
    /// </summary>
    public async Task<LoadCheckWarning?> ForAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        if (shipment.CarriedBy.CarrierId is not null
            || (await transportOrders.ListAsync(new TransportOrdersByIdsSpec([shipment.TransportOrderId]), cancellationToken)).FirstOrDefault() is not { } order
            || CollectionDate.Of(order.Details) is not { } date)
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

        var load = loads.Of(day, await loads.DemandByDayAsync(cancellationToken));
        return load.Over ? new LoadCheckWarning(date, OutsideHorizon: false, load) : null;
    }
}
