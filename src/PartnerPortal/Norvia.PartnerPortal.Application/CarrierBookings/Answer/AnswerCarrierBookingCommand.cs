using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierBookings.Answer;

/// <summary>
/// Sends a Carrier Persona's confirmation or rejection of a request in its inbox to Norvia, and keeps what Norvia replies:
/// a recorded answer closes the request, and BookingNotOpen shows it as withdrawn by Norvia. Not found when the Persona has
/// no such Carrier Persona or request.
/// </summary>
public sealed record AnswerCarrierBookingCommand(PersonaId PersonaId, string CarrierBookingNumber, CarrierAnswer Answer)
    : ICommand<Result<AnsweredCarrierBooking>>;
