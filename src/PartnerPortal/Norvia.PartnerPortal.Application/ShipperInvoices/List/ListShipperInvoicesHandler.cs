using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Domain.ShipperInvoices;

namespace Norvia.PartnerPortal.Application.ShipperInvoices.List;

public sealed class ListShipperInvoicesHandler(IReadRepository<ShipperInvoice> repository)
    : IQueryHandler<ListShipperInvoicesQuery, IReadOnlyList<ShipperInvoiceView>>
{
    public async ValueTask<IReadOnlyList<ShipperInvoiceView>> Handle(ListShipperInvoicesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoices = await repository.ListAsync(new ShipperInvoicesOfPersonaSpec(query.PersonaId), cancellationToken);
        return [.. invoices.Select(ShipperInvoiceView.From)];
    }
}
