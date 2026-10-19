using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;

namespace Norvia.Erp.Api.CarrierCosts;

/// <summary>
/// <c>CarrierCostIncurred</c>, version 1, as the Canonical Model has it (ADR 0003). The ERP billing API accepts nothing else:
/// a member outside the model, or a missing one, is refused.
/// </summary>
public sealed record CarrierCostIncurredRequest(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string CarrierBookingNumber,
    string CarrierBookingReference,
    string ShipmentNumber,
    string TransportOrderNumber,
    string CarrierId,
    CarrierCostRequest CarrierCost,
    DateTimeOffset DeliveryTime);

public sealed record CarrierCostRequest(
    string Currency,
    decimal Total,
    IReadOnlyList<CarrierCostChargeLineRequest> ChargeLines,
    CarrierCostSourceRequest Source,
    DateTimeOffset CalculatedAt);

public sealed record CarrierCostChargeLineRequest(ChargeLineType Type, decimal Amount);

public sealed record CarrierCostSourceRequest(CarrierCostSourceKind Kind, CarrierRateCardVersionRequest? RateCardVersion = null, string? Remark = null);

public sealed record CarrierRateCardVersionRequest(string RateCardId, int Version);
