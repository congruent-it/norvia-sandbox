namespace Norvia.PartnerPortal.Application.Bookings;

/// <summary>A time window in the local time of the address it belongs to, as a party fills it in.</summary>
public sealed record LocalWindow(DateTime? From = null, DateTime? Until = null);
