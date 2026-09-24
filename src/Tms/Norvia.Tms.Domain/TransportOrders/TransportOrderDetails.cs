namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// Everything the Shipper submitted for a Transport Order, kept exactly as received. Fields are optional here because
/// whether a booking is complete is decided by the Transport Order rules, and a rejected booking is kept as submitted.
/// </summary>
public sealed record TransportOrderDetails(
    string? ShipperReference,
    CollectionDetails? Collection,
    DeliveryDetails? Delivery,
    IReadOnlyList<GoodsLine> GoodsLines,
    string? Instructions)
{
    public bool Equals(TransportOrderDetails? other) =>
        other is not null
        && ShipperReference == other.ShipperReference
        && Equals(Collection, other.Collection)
        && Equals(Delivery, other.Delivery)
        && GoodsLines.SequenceEqual(other.GoodsLines)
        && Instructions == other.Instructions;

    public override int GetHashCode() =>
        HashCode.Combine(ShipperReference, Collection, Delivery, GoodsLines.Count, Instructions);
}

public sealed record CollectionDetails(Address? Address, TimeWindow? Window);

public sealed record DeliveryDetails(Address? Address, Consignee? Consignee, TimeWindow? Window);

/// <param name="Country">ISO 3166-1 alpha-2 country code.</param>
public sealed record Address(string? Street, string? Postcode, string? City, string? Country);

public sealed record TimeWindow(DateTimeOffset? Earliest, DateTimeOffset? Latest);

public sealed record Consignee(string? Name, string? ContactName, string? Phone, string? Email);

/// <param name="WeightKg">Weight of the whole line in kilograms.</param>
public sealed record GoodsLine(string? PackageType, int? Quantity, decimal? WeightKg, Dimensions? DimensionsCm);

public sealed record Dimensions(decimal? Length, decimal? Width, decimal? Height);
