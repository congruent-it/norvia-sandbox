using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>What <c>CarrierCostIncurred</c> carries, as the domain takes it.</summary>
public sealed record CarrierCostData(
    string CarrierBookingNumber,
    string CarrierBookingReference,
    CarrierId CarrierId,
    string ShipmentNumber,
    string TransportOrderNumber,
    CarrierCostCharge Charge,
    CarrierCostSource Source,
    DateTimeOffset DeliveryTime,
    string CorrelationId);
