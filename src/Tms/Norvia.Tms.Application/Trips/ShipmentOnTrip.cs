using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Trips;

/// <summary>A Shipment with the Transport Order details its Stops are derived from (ADR 0007).</summary>
public sealed record ShipmentOnTrip(Shipment Shipment, TransportOrderDetails Details)
{
    /// <summary>What the Shipment asks of a Vehicle: its weight and pallet places.</summary>
    public FleetLoad Load => LoadCheck.DemandOf(Details);

    /// <summary>The day the Shipment collects, as P10 defines it; null when it has no collection time.</summary>
    public DateOnly? CollectionDay => CollectionDate.Of(Details);

    /// <summary>The delivery window that counts: the last agreed one, otherwise the Shipper's.</summary>
    public TimeWindow? DeliveryWindow => Shipment.DeliveryWindowOf(Details);

    /// <summary>True when the Shipment's delivery window is known and doesn't include <paramref name="day"/>.</summary>
    public bool DeliversOutside(DateOnly day) =>
        DeliveryWindow is { Earliest: { } earliest, Latest: { } latest }
        && (day < CollectionDate.DayOf(earliest) || day > CollectionDate.DayOf(latest));
}
