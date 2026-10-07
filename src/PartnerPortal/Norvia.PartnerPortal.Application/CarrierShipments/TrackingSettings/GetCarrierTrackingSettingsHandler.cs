using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;

public sealed class GetCarrierTrackingSettingsHandler(IReadRepository<CarrierTrackingSettings> repository, IPersonaDirectory personas)
    : IQueryHandler<GetCarrierTrackingSettingsQuery, Result<CarrierTrackingSettingsView>>
{
    public async ValueTask<Result<CarrierTrackingSettingsView>> Handle(GetCarrierTrackingSettingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (personas.Find(query.PersonaId) is not { Carrier: not null } carrier)
        {
            return Result.NotFound($"There is no Carrier Persona '{query.PersonaId}'.");
        }

        var chosen = await repository.FirstOrDefaultAsync(new CarrierTrackingSettingsOfPersonaSpec(carrier.Id), cancellationToken);
        return CarrierTrackingSettingsView.From(CarrierTrackingPolicies.InForce(carrier, chosen));
    }
}
