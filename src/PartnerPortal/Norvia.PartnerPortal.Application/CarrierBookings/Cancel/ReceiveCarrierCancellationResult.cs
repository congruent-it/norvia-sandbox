using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierBookings.Cancel;

public sealed record ReceiveCarrierCancellationResult(string CarrierBookingNumber, CarrierCancellationOutcome Outcome);
