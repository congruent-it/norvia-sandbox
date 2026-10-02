using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierBookings.Answer;

/// <summary>What became of a Carrier's answer.</summary>
/// <param name="Request">The request as the inbox now has it.</param>
/// <param name="Reply">Norvia's reply; null when the request was no longer open in the inbox, so nothing was sent.</param>
/// <param name="Recorded">False when Norvia replied but the portal couldn't keep it: the inbox still shows the request as it was.</param>
public sealed record AnsweredCarrierBooking(CarrierBookingRequestView Request, NorviaReply? Reply, bool Recorded = true);
