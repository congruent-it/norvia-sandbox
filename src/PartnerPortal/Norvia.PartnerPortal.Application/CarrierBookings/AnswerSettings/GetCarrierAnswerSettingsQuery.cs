using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;

/// <summary>How the Carrier Persona answers its booking requests now. Not found when there is no such Carrier Persona.</summary>
public sealed record GetCarrierAnswerSettingsQuery(PersonaId PersonaId) : IQuery<Result<CarrierAnswerSettingsView>>;
