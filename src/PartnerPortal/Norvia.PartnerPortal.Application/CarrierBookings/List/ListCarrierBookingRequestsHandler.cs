using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierBookings.List;

public sealed class ListCarrierBookingRequestsHandler(IReadRepository<CarrierBookingRequest> repository)
    : IQueryHandler<ListCarrierBookingRequestsQuery, IReadOnlyList<CarrierBookingRequestView>>
{
    public async ValueTask<IReadOnlyList<CarrierBookingRequestView>> Handle(ListCarrierBookingRequestsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var requests = await repository.ListAsync(new CarrierBookingRequestsOfPersonaSpec(query.PersonaId), cancellationToken);
        return [.. requests.Select(CarrierBookingRequestView.From)];
    }
}
