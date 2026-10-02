namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>A Carrier Persona's own booking reference for a request it confirms automatically.</summary>
public static class BookingReferencePattern
{
    /// <summary>
    /// <c>VDT-240917-0042</c>: the Carrier's prefix, the day it confirmed and a sequence taken from the Carrier booking number,
    /// which is unique, so the Carrier's references are too.
    /// </summary>
    public static string Generate(string prefix, DateTimeOffset confirmedAt, string carrierBookingNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierBookingNumber);
        return $"{prefix}-{confirmedAt.UtcDateTime:yyMMdd}-{SequenceOf(carrierBookingNumber):0000}";
    }

    private static long SequenceOf(string carrierBookingNumber)
    {
        var prefixLength = carrierBookingNumber.AsSpan().TrimEnd("0123456789").Length;
        var tail = carrierBookingNumber.AsSpan(prefixLength);
        return tail.Length > 0 && long.TryParse(tail, out var number) ? number % 10_000 : 0;
    }
}
