namespace Norvia.PartnerPortal.Application.CarrierBookings.Cancel;

/// <param name="Applied">True when the request in the inbox changed.</param>
/// <param name="Repeated">True when the inbox already showed the booking ended by Norvia, so nothing changed.</param>
public sealed record ReceiveCarrierCancellationResult(string CarrierBookingNumber, bool Applied, bool Repeated);
