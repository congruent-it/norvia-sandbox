namespace Norvia.Tms.Application.Exceptions.CancelCarrierBooking;

/// <summary>What cancelling a Carrier booking did.</summary>
/// <param name="CorrelationId">The cancellation's own Message trail; a repeat answers with the trail of the cancellation it repeats.</param>
/// <param name="Repeat">True when the cancellation was sent again under its key, and answered as before without cancelling again.</param>
public sealed record CancelCarrierBookingResult(
    string ExceptionNumber, string ShipmentNumber, string CarrierBookingNumber, string CorrelationId, bool Repeat = false);
