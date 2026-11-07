using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Application.Agents.RejectionReason;

/// <summary>
/// Builds the facts of a line that the Carrier can see, field by field (ADR 0040, #473). The reason the Agent drafts leaves Norvia, so what
/// this view does not name never reaches that Agent: it reads the agreed Carrier cost's total and nothing about where the price came from,
/// and a likely correct reference only from the Carrier's own Open Carrier costs.
/// </summary>
public sealed class GetCarrierVisibleLineFactsHandler(IReadRepository<CarrierInvoice> invoices, IReadRepository<CarrierCost> costs)
    : IQueryHandler<GetCarrierVisibleLineFactsQuery, Result<CarrierVisibleLineFactsView>>
{
    public async ValueTask<Result<CarrierVisibleLineFactsView>> Handle(GetCarrierVisibleLineFactsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoice = await invoices.FirstOrDefaultAsync(new CarrierInvoiceByNumberSpec(query.CarrierId, query.InvoiceNumber), cancellationToken);
        if (invoice is null)
        {
            return CarrierInvoiceRefused.Because<CarrierVisibleLineFactsView>(CarrierInvoiceRefusal.InvoiceNotFound(query.CarrierId.Value, query.InvoiceNumber));
        }

        var line = invoice.Lines.FirstOrDefault(candidate => candidate.CarrierBookingReference == query.CarrierBookingReference);
        if (line is null)
        {
            return CarrierInvoiceRefused.Because<CarrierVisibleLineFactsView>(CarrierInvoiceRefusal.LineNotFound(query.InvoiceNumber, query.CarrierBookingReference));
        }

        var cost = line.CarrierBookingNumber is { } number
            ? await costs.FirstOrDefaultAsync(new CarrierCostByBookingNumberSpec(number), cancellationToken)
            : null;

        return new CarrierVisibleLineFactsView(
            invoice.CarrierId.Value,
            invoice.InvoiceNumber,
            line.CarrierBookingReference,
            line.Result.ToString(),
            line.Amount,
            CarrierCostCharge.Currency,
            cost?.Total,
            line.Result == CarrierInvoiceLineResult.UnknownBooking ? await LikelyReferenceAsync(invoice, line, cancellationToken) : null,
            line.Result == CarrierInvoiceLineResult.Duplicate && cost is not null ? await EarlierInvoiceAsync(invoice, line, cost, cancellationToken) : null);
    }

    private async Task<string?> LikelyReferenceAsync(CarrierInvoice invoice, CarrierInvoiceLine line, CancellationToken cancellationToken)
    {
        var open = await costs.ListAsync(new OpenCarrierCostsOfCarrierSpec(invoice.CarrierId), cancellationToken);

        // The spec already asks for this Carrier's costs; the check is the boundary itself, so no other Carrier's reference can get out.
        var ranked = CandidateCarrierCosts.Rank(line.CarrierBookingReference, line.Amount, invoice.InvoiceDate, open.Where(cost => cost.CarrierId == invoice.CarrierId));
        return ranked.Count > 0 ? ranked[0].Cost.CarrierBookingReference : null;
    }

    private async Task<string?> EarlierInvoiceAsync(CarrierInvoice invoice, CarrierInvoiceLine line, CarrierCost cost, CancellationToken cancellationToken)
    {
        var all = await invoices.ListAsync(new CarrierInvoicesOfCarrierSpec(invoice.CarrierId), cancellationToken);
        return EarlierLine.Find(all, cost.Id.CarrierBookingNumber, invoice.InvoiceNumber, line.CarrierBookingReference)?.Invoice.InvoiceNumber;
    }
}
