namespace Norvia.PartnerPortal.Application.Bookings;

/// <param name="Country">A country's name or its ISO 3166-1 alpha-2 code.</param>
public sealed record BookingAddress(string? Street = null, string? Postcode = null, string? City = null, string? Country = null);
