namespace Norvia.PartnerPortal.Application.Bookings;

/// <summary>
/// A booking as the party fills it in, before it is sent. Every field may be empty: which ones a booking needs is for
/// Norvia to decide, and Norvia's answer names every one that is missing or wrong.
/// </summary>
public sealed record BookingRequest
{
    public string? ShipperReference { get; init; }

    public BookingAddress CollectionAddress { get; init; } = new();

    public LocalWindow CollectionWindow { get; init; } = new();

    public BookingConsignee Consignee { get; init; } = new();

    public BookingAddress DeliveryAddress { get; init; } = new();

    /// <summary>Optional: the Consignee's constraints, if any.</summary>
    public LocalWindow DeliveryWindow { get; init; } = new();

    public IReadOnlyList<BookingGoodsLine> GoodsLines { get; init; } = [];

    public string? Instructions { get; init; }
}
