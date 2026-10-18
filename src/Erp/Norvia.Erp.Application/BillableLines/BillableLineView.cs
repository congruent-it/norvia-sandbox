namespace Norvia.Erp.Application.BillableLines;

/// <summary>A Billable line as the ERP answers with it, in the Canonical Model's terms.</summary>
public sealed record BillableLineView(
    string TransportOrderNumber,
    string ShipperId,
    string ShipperReference,
    string ShipmentNumber,
    DateTimeOffset DeliveryTime,
    FreightChargeView FreightCharge,
    bool ReleasedWithoutProofOfDelivery,
    string IntakeCorrelationId,
    string BillingCorrelationId,
    DateTimeOffset RecordedAt,
    string Status);

public sealed record FreightChargeView(string Currency, decimal Total, IReadOnlyList<ChargeLineView> ChargeLines, string RateCardId, int RateCardVersion);

public sealed record ChargeLineView(string Type, decimal Amount);
