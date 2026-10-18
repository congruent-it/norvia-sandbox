using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.BillableLines;

/// <summary>What <c>TransportOrderBillable</c> carries, as the domain takes it.</summary>
public sealed record BillableLineData(
    string TransportOrderNumber,
    ShipperId ShipperId,
    string ShipperReference,
    string ShipmentNumber,
    DateTimeOffset DeliveryTime,
    FreightCharge FreightCharge,
    bool ReleasedWithoutProofOfDelivery,
    string IntakeCorrelationId,
    string BillingCorrelationId);
