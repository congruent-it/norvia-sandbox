using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.CreditNotes;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.CreditNotes.List;

public sealed class ListCreditNotesHandler(IReadRepository<CreditNote> creditNotes, IReadRepository<Shipper> shippers)
    : IQueryHandler<ListCreditNotesQuery, IReadOnlyList<CreditNoteView>>
{
    public async ValueTask<IReadOnlyList<CreditNoteView>> Handle(ListCreditNotesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        InvoiceNumber? invoice = string.IsNullOrWhiteSpace(query.InvoiceNumber) ? null : new InvoiceNumber(query.InvoiceNumber);
        var issued = await creditNotes.ListAsync(new CreditNotesNewestFirstSpec(invoice), cancellationToken);
        var names = (await shippers.ListAsync(new ShippersSpec(), cancellationToken)).ToDictionary(shipper => shipper.Id, shipper => shipper.Name);

        return [.. issued.Select(note => CreditNoteViews.Of(note, names.GetValueOrDefault(note.ShipperId, note.ShipperId.Value)))];
    }
}
