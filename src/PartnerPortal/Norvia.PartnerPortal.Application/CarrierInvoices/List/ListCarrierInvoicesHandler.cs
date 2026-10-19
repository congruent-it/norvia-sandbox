using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Domain.CarrierInvoices;

namespace Norvia.PartnerPortal.Application.CarrierInvoices.List;

public sealed class ListCarrierInvoicesHandler(IReadRepository<CarrierInvoice> invoices, IReadRepository<CarrierInvoiceOutcome> outcomes)
    : IQueryHandler<ListCarrierInvoicesQuery, IReadOnlyList<CarrierInvoiceView>>
{
    public async ValueTask<IReadOnlyList<CarrierInvoiceView>> Handle(ListCarrierInvoicesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sent = await invoices.ListAsync(new CarrierInvoicesOfPersonaSpec(query.PersonaId), cancellationToken);
        var told = (await outcomes.ListAsync(new CarrierInvoiceOutcomesOfPersonaSpec(query.PersonaId), cancellationToken))
            .ToDictionary(outcome => outcome.InvoiceNumber, StringComparer.Ordinal);
        return [.. sent.Select(invoice => CarrierInvoiceView.From(invoice, told.GetValueOrDefault(invoice.InvoiceNumber)))];
    }
}
