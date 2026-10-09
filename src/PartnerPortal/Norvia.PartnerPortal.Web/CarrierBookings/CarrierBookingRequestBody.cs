namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>The booking request in the Carrier booking format (P4), as the P4 booking Logic App sends it.</summary>
public sealed record CarrierBookingRequestBody(
    string? CarrierBookingNumber,
    string? ShipmentNumber,
    string? CollectionStreet,
    string? CollectionPostcode,
    string? CollectionCity,
    string? CollectionCountry,
    DateTime? CollectFrom,
    DateTime? CollectUntil,
    string? DeliveryStreet,
    string? DeliveryPostcode,
    string? DeliveryCity,
    string? DeliveryCountry,
    DateTime? DeliverFrom,
    DateTime? DeliverUntil,
    string? ConsigneeName,
    IReadOnlyList<CarrierBookingGoodsLineBody>? Goods,
    CarrierCostBody? CarrierCost = null);

public sealed record CarrierCostBody(string? Currency, decimal? Total, IReadOnlyList<CarrierChargeLineBody>? ChargeLines, bool IsSpot);

public sealed record CarrierChargeLineBody(string? Type, decimal? Amount);

public sealed record CarrierBookingGoodsLineBody(
    string? PackageType,
    int? Quantity,
    decimal? WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm);
