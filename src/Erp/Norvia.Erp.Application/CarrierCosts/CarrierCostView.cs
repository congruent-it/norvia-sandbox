using Norvia.Erp.Application.BillableLines;

namespace Norvia.Erp.Application.CarrierCosts;

/// <summary>A Carrier cost as the ERP answers with it, in the Canonical Model's terms.</summary>
public sealed record CarrierCostView(
    string CarrierBookingNumber,
    string CarrierBookingReference,
    string CarrierId,
    string ShipmentNumber,
    string TransportOrderNumber,
    string Currency,
    decimal Total,
    IReadOnlyList<ChargeLineView> ChargeLines,
    CarrierCostSourceView Source,
    DateTimeOffset DeliveryTime,
    string CorrelationId,
    DateTimeOffset RecordedAt,
    string Status);

/// <summary>Where a Carrier cost came from: <c>CarrierRateCard</c> with the version, or <c>Spot</c> with the Planner's remark.</summary>
public sealed record CarrierCostSourceView(string Kind, string? RateCardId, int? RateCardVersion, string? Remark);
