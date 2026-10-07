using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Billing.Release;

/// <summary>What releasing a Transport Order without a Proof of delivery did.</summary>
/// <param name="BillingState">Billable when the release completed the order, otherwise still Not yet billable.</param>
/// <param name="CorrelationId">The release's own Message trail, which <c>TransportOrderBillable</c> carries if it completed the order.</param>
public sealed record ReleaseWithoutProofOfDeliveryResult(
    string TransportOrderNumber,
    string ShipmentNumber,
    BillingState BillingState,
    string CorrelationId);
