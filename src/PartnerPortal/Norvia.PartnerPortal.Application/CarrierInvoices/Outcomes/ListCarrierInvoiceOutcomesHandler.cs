using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Domain.CarrierInvoices;

namespace Norvia.PartnerPortal.Application.CarrierInvoices.Outcomes;

public sealed class ListCarrierInvoiceOutcomesHandler(IReadRepository<CarrierInvoiceOutcome> outcomes)
    : IQueryHandler<ListCarrierInvoiceOutcomesQuery, IReadOnlyList<CarrierInvoiceOutcome>>
{
    public async ValueTask<IReadOnlyList<CarrierInvoiceOutcome>> Handle(ListCarrierInvoiceOutcomesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await outcomes.ListAsync(new CarrierInvoiceOutcomesOfPersonaSpec(query.PersonaId), cancellationToken);
    }
}
