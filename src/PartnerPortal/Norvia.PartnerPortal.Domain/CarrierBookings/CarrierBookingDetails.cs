namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>
/// What a booking request asks of the Carrier, as Norvia sent it in the Carrier booking format (P4): where and when to
/// collect and deliver, who receives the goods, and the goods lines. It never names the Shipper.
/// </summary>
/// <param name="ConsigneeName">Who receives the goods, the only thing the Carrier is told about the Consignee.</param>
/// <param name="CarrierCost">What Norvia offers the Carrier for the booking; null for a request that carries none.</param>
public sealed record CarrierBookingDetails(
    CarrierBookingStop Collection,
    CarrierBookingStop Delivery,
    string? ConsigneeName,
    IReadOnlyList<CarrierBookingGoodsLine> Goods,
    CarrierCost? CarrierCost = null);

/// <summary>The price Norvia offers the Carrier, frozen at release. By confirming, the Carrier accepts it. It is never the Shipper's Freight Charge.</summary>
/// <param name="IsSpot">True when the Planner entered the price by hand.</param>
public sealed record CarrierCost(string Currency, decimal Total, IReadOnlyList<CarrierChargeLine> ChargeLines, bool IsSpot);

/// <param name="Type">The kind of charge line, such as <c>Freight</c> or <c>MinimumChargeAdjustment</c>.</param>
public sealed record CarrierChargeLine(string Type, decimal Amount);

/// <summary>An address with the window it is visited in.</summary>
/// <param name="Country">The ISO 3166-1 alpha-3 country code.</param>
/// <param name="From">The window's start, as local time at the address, without an offset.</param>
/// <param name="Until">The window's end, as local time at the address, without an offset.</param>
public sealed record CarrierBookingStop(string? Street, string? Postcode, string? City, string? Country, DateTime? From, DateTime? Until);

public sealed record CarrierBookingGoodsLine(string? PackageType, int? Quantity, decimal? WeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm);
