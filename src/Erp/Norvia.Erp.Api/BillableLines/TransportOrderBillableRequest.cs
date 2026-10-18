namespace Norvia.Erp.Api.BillableLines;

/// <summary>
/// <c>TransportOrderBillable</c>, version 1, as the Canonical Model has it (ADR 0003). The ERP billing API accepts nothing else:
/// a member outside the model, or a missing one, is refused.
/// </summary>
public sealed record TransportOrderBillableRequest(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string IntakeCorrelationId,
    string TransportOrderNumber,
    string ShipperId,
    string ShipperReference,
    string ShipmentNumber,
    DateTimeOffset DeliveryTime,
    FreightChargeRequest FreightCharge,
    bool ReleasedWithoutProofOfDelivery);

public sealed record FreightChargeRequest(string Currency, decimal Total, IReadOnlyList<ChargeLineRequest> ChargeLines, RateCardVersionRequest RateCardVersion);

public sealed record ChargeLineRequest(Norvia.Erp.Domain.BillableLines.ChargeLineType Type, decimal Amount);

public sealed record RateCardVersionRequest(string RateCardId, int Version);
