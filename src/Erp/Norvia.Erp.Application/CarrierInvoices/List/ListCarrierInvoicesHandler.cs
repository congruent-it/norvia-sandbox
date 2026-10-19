using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierInvoices.List;

public sealed class ListCarrierInvoicesHandler(IReadRepository<CarrierInvoice> invoices, IReadRepository<Carrier> carriers)
    : IQueryHandler<ListCarrierInvoicesQuery, IReadOnlyList<CarrierInvoiceListItem>>
{
    public async ValueTask<IReadOnlyList<CarrierInvoiceListItem>> Handle(ListCarrierInvoicesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var received = await invoices.ListAsync(new CarrierInvoicesNewestFirstSpec(), cancellationToken);
        var names = (await carriers.ListAsync(new CarriersSpec(), cancellationToken)).ToDictionary(carrier => carrier.Id, carrier => carrier.Name);

        return
        [
            .. received.Select(invoice => new CarrierInvoiceListItem(
                names.GetValueOrDefault(invoice.CarrierId, invoice.CarrierId.Value),
                CarrierInvoiceViews.Of(invoice))),
        ];
    }
}
