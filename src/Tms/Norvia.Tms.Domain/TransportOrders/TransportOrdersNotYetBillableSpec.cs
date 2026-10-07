using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>The Transport-only orders that aren't Billable yet and have their Shipment (P9), for the list Customer service follows up.</summary>
public sealed class TransportOrdersNotYetBillableSpec : Specification<TransportOrder>
{
    public TransportOrdersNotYetBillableSpec() =>
        Query.Where(order => order.BillingState == BillingState.NotYetBillable && order.ShipmentNumber != null)
            .AsNoTracking();
}
