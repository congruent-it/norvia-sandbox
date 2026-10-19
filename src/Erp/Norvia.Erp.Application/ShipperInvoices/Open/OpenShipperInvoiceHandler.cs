using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.ShipperInvoices.Open;

public sealed class OpenShipperInvoiceHandler(IReadRepository<ShipperInvoice> invoices, IReadRepository<Shipper> shippers)
    : IQueryHandler<OpenShipperInvoiceQuery, ShipperInvoiceView?>
{
    public async ValueTask<ShipperInvoiceView?> Handle(OpenShipperInvoiceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.InvoiceNumber)
            || await invoices.FirstOrDefaultAsync(new ShipperInvoiceByNumberSpec(new InvoiceNumber(query.InvoiceNumber)), cancellationToken) is not { } invoice)
        {
            return null;
        }

        var shipper = await shippers.FirstOrDefaultAsync(new ShipperByIdSpec(invoice.ShipperId), cancellationToken);
        return ShipperInvoiceViews.Of(invoice, shipper?.Name ?? invoice.ShipperId.Value);
    }
}
