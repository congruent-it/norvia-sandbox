using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierShipments.List;

public sealed class ListCarrierShipmentsHandler(IReadRepository<CarrierBookingRequest> repository)
    : IQueryHandler<ListCarrierShipmentsQuery, IReadOnlyList<CarrierShipmentView>>
{
    public async ValueTask<IReadOnlyList<CarrierShipmentView>> Handle(ListCarrierShipmentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var requests = await repository.ListAsync(new CarrierShipmentsOfPersonaSpec(new PersonaId(query.PersonaId)), cancellationToken);
        return [.. requests.Select(CarrierShipmentView.From)];
    }
}
