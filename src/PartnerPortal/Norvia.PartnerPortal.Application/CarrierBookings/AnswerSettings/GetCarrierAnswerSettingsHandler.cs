using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;

public sealed class GetCarrierAnswerSettingsHandler(IReadRepository<CarrierAnswerSettings> repository, IPersonaDirectory personas)
    : IQueryHandler<GetCarrierAnswerSettingsQuery, Result<CarrierAnswerSettingsView>>
{
    public async ValueTask<Result<CarrierAnswerSettingsView>> Handle(GetCarrierAnswerSettingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (personas.Find(new PersonaId(query.PersonaId)) is not { Carrier: not null } carrier)
        {
            return Result.NotFound($"There is no Carrier Persona '{query.PersonaId}'.");
        }

        var chosen = await repository.FirstOrDefaultAsync(new CarrierAnswerSettingsOfPersonaSpec(carrier.Id), cancellationToken);
        return CarrierAnswerSettingsView.From(CarrierAnswerPolicies.InForce(carrier, chosen));
    }
}
