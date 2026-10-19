using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.ShipperInvoices.List;

public sealed class ListShipperInvoicesHandler(IReadRepository<ShipperInvoice> invoices, IReadRepository<Shipper> shippers)
    : IQueryHandler<ListShipperInvoicesQuery, IReadOnlyList<ShipperInvoiceView>>
{
    public async ValueTask<IReadOnlyList<ShipperInvoiceView>> Handle(ListShipperInvoicesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var issued = await invoices.ListAsync(new ShipperInvoicesNewestFirstSpec(), cancellationToken);
        var names = (await shippers.ListAsync(new ShippersSpec(), cancellationToken)).ToDictionary(shipper => shipper.Id, shipper => shipper.Name);

        return [.. issued.Select(invoice => ShipperInvoiceViews.Of(invoice, names.GetValueOrDefault(invoice.ShipperId, invoice.ShipperId.Value)))];
    }
}
