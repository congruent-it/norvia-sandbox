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
        var sentNumbers = sent.Select(invoice => invoice.InvoiceNumber).ToHashSet(StringComparer.Ordinal);
        // The invoices the Persona sent another way, such as the Simulator through the Partner API, are known by their outcome alone.
        var elsewhere = told.Values.Where(outcome => !sentNumbers.Contains(outcome.InvoiceNumber)).Select(CarrierInvoiceView.FromOutcome);
        return
        [
            .. sent.Select(invoice => CarrierInvoiceView.From(invoice, told.GetValueOrDefault(invoice.InvoiceNumber))).Concat(elsewhere)
                .OrderByDescending(invoice => invoice.SubmittedAt),
        ];
    }
}
