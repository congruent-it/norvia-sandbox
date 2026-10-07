using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;

/// <summary>
/// Changes how the Carrier Persona answers the booking requests that reach it from now on; a request already in its inbox
/// keeps the answer it was received with. Not found when there is no such Carrier Persona, invalid when the delay is out of range.
/// </summary>
public sealed record ChangeCarrierAnswerSettingsCommand(PersonaId PersonaId, CarrierAnswerMode Mode, CarrierRejectionReason RejectionReason, int DelaySeconds)
    : ICommand<Result<CarrierAnswerSettingsView>>;
