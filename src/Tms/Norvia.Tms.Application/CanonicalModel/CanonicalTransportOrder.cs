using System.Text.Json.Serialization;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.CanonicalModel;

/// <summary>
/// The Canonical Model <c>TransportOrder</c>, version 1 (contracts/canonical-model/transport-order.v1.schema.json).
/// </summary>
public sealed record CanonicalTransportOrder(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TransportOrderNumber,
    string ShipperId,
    string? ShipperReference,
    CanonicalCollectionDetails? Collection,
    CanonicalDeliveryDetails? Delivery,
    IReadOnlyList<CanonicalGoodsLine>? GoodsLines,
    string? Instructions)
{
    public bool Equals(CanonicalTransportOrder? other) =>
        other is not null
        && TransportOrderNumber == other.TransportOrderNumber
        && ShipperId == other.ShipperId
        && ShipperReference == other.ShipperReference
        && Equals(Collection, other.Collection)
        && Equals(Delivery, other.Delivery)
        && (GoodsLines ?? []).SequenceEqual(other.GoodsLines ?? [])
        && Instructions == other.Instructions;

    public override int GetHashCode() => HashCode.Combine(TransportOrderNumber, ShipperId, ShipperReference);

    public static CanonicalTransportOrder From(TransportOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var details = order.Details;
        return new CanonicalTransportOrder(
            order.Number.Value,
            order.ShipperId.Value,
            details.ShipperReference,
            details.Collection is { } collection
                ? new CanonicalCollectionDetails(CanonicalAddress.From(collection.Address), CanonicalTimeWindow.From(collection.Window))
                : null,
            details.Delivery is { } delivery
                ? new CanonicalDeliveryDetails(CanonicalAddress.From(delivery.Address), CanonicalConsignee.From(delivery.Consignee), CanonicalTimeWindow.From(delivery.Window))
                : null,
            [.. details.GoodsLines.Select(CanonicalGoodsLine.From)],
            details.Instructions);
    }

    public TransportOrderDetails ToDetails() => new(
        ShipperReference,
        Collection is { } collection ? new CollectionDetails(collection.Address?.ToDomain(), collection.Window?.ToDomain()) : null,
        Delivery is { } delivery ? new DeliveryDetails(delivery.Address?.ToDomain(), delivery.Consignee?.ToDomain(), delivery.Window?.ToDomain()) : null,
        [.. (GoodsLines ?? []).Select(line => line.ToDomain())],
        Instructions);
}

public sealed record CanonicalCollectionDetails(CanonicalAddress? Address, CanonicalTimeWindow? Window);

public sealed record CanonicalDeliveryDetails(CanonicalAddress? Address, CanonicalConsignee? Consignee, CanonicalTimeWindow? Window);

public sealed record CanonicalAddress(string? Street, string? Postcode, string? City, string? Country)
{
    public static CanonicalAddress? From(Address? address) =>
        address is null ? null : new(address.Street, address.Postcode, address.City, address.Country);

    public Address ToDomain() => new(Street, Postcode, City, Country);
}

public sealed record CanonicalTimeWindow(DateTimeOffset? Earliest, DateTimeOffset? Latest)
{
    public static CanonicalTimeWindow? From(TimeWindow? window) =>
        window is null ? null : new(window.Earliest, window.Latest);

    public TimeWindow ToDomain() => new(Earliest, Latest);
}

public sealed record CanonicalConsignee(string? Name, string? ContactName, string? Phone, string? Email)
{
    public static CanonicalConsignee? From(Consignee? consignee) =>
        consignee is null ? null : new(consignee.Name, consignee.ContactName, consignee.Phone, consignee.Email);

    public Consignee ToDomain() => new(Name, ContactName, Phone, Email);
}

public sealed record CanonicalGoodsLine(string? PackageType, int? Quantity, decimal? WeightKg, CanonicalDimensions? DimensionsCm)
{
    public static CanonicalGoodsLine From(GoodsLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new(line.PackageType, line.Quantity, line.WeightKg, CanonicalDimensions.From(line.DimensionsCm));
    }

    public GoodsLine ToDomain() => new(PackageType, Quantity, WeightKg, DimensionsCm?.ToDomain());
}

public sealed record CanonicalDimensions(decimal? Length, decimal? Width, decimal? Height)
{
    public static CanonicalDimensions? From(Dimensions? dimensions) =>
        dimensions is null ? null : new(dimensions.Length, dimensions.Width, dimensions.Height);

    public Dimensions ToDomain() => new(Length, Width, Height);
}
