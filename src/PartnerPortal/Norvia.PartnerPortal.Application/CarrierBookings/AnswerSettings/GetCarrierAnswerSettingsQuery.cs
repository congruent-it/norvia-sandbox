using Ardalis.Result;
using Mediator;

namespace Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;

/// <summary>How the Carrier Persona answers its booking requests now. Not found when there is no such Carrier Persona.</summary>
public sealed record GetCarrierAnswerSettingsQuery(string PersonaId) : IQuery<Result<CarrierAnswerSettingsView>>;
