namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>The cancellation in the Carrier cancellation format (P7), as the Carrier cancellation Logic App sends it.</summary>
public sealed record CarrierCancellationBody(
    string? CarrierBookingNumber,
    string? CarrierBookingReference,
    string? ShipmentNumber,
    string? Kind,
    string? ReasonText);
