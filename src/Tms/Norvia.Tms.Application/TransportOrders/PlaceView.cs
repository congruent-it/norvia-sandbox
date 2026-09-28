using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders;

/// <summary>A place goods are collected from or delivered to, as a Planner reads it at a glance: without the street.</summary>
/// <param name="Country">ISO 3166-1 alpha-2 country code.</param>
public sealed record PlaceView(string? Postcode, string? City, string? Country)
{
    /// <summary>The address's place; null for no address.</summary>
    public static PlaceView? Of(Address? address) =>
        address is null ? null : new(address.Postcode, address.City, address.Country);
}
