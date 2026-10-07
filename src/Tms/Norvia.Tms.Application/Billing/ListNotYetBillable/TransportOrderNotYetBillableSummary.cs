using Norvia.Tms.Application.TransportOrders;

namespace Norvia.Tms.Application.Billing.ListNotYetBillable;

/// <summary>A Delivered Transport-only order in Customer service's list of orders not yet billable (P9).</summary>
/// <param name="DeliveredAt">The event time of the Delivered Tracking event that moved its Shipment to Delivered.</param>
/// <param name="BlockingReasons">Each condition still missing, never empty.</param>
public sealed record TransportOrderNotYetBillableSummary(
    string TransportOrderNumber,
    string ShipmentNumber,
    ShipperView Shipper,
    string? ShipperReference,
    DateTimeOffset DeliveredAt,
    IReadOnlyList<BillingBlockingReason> BlockingReasons);
