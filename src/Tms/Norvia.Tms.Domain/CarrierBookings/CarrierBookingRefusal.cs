namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// Why the TMS refused a Carrier's answer, with a readable message always and a stable <see cref="CarrierBookingRefusalCode"/>
/// where there is one; the wrong-Carrier rule has none, as it is refused with no code of its own (P4's answer rules).
/// </summary>
public sealed record CarrierBookingRefusal(CarrierBookingRefusalCode? Code, string Message);
