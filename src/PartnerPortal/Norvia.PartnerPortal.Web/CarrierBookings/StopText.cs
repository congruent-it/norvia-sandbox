using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>Words for a stop of a booking request: its address on one line, and its window in the address's own time.</summary>
public static class StopText
{
    public static string Address(CarrierBookingStop stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        var postcodeAndCity = string.Join(' ', new[] { stop.Postcode, stop.City }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.Join(", ", new[] { stop.Street, postcodeAndCity, stop.Country }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>The window as "from – until"; null when the request names neither end.</summary>
    public static string? Window(CarrierBookingStop stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        return (stop.From, stop.Until) switch
        {
            (null, null) => null,
            ({ } from, null) => $"from {PortalTime.FormatLocal(from)}",
            (null, { } until) => $"until {PortalTime.FormatLocal(until)}",
            ({ } from, { } until) => $"{PortalTime.FormatLocal(from)} – {PortalTime.FormatLocal(until)}",
        };
    }
}
