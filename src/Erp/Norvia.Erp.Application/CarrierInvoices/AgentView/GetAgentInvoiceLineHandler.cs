using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierInvoices.AgentView;

/// <summary>
/// Builds the Finance agent's view of a line. The ERP computes the facts (the difference, the tolerance, the earlier line of a Duplicate
/// and the ranked candidates of an Unknown booking) so the Agent explains and never works them out itself (ADR 0040). The view is built
/// field by field: what it does not name never reaches an Agent.
/// </summary>
public sealed class GetAgentInvoiceLineHandler(
    IReadRepository<CarrierInvoice> invoices,
    IReadRepository<CarrierCost> costs,
    IReadRepository<Carrier> carriers,
    IMatchingTolerance tolerance)
    : IQueryHandler<GetAgentInvoiceLineQuery, Result<AgentInvoiceLineView>>
{
    public async ValueTask<Result<AgentInvoiceLineView>> Handle(GetAgentInvoiceLineQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoice = await invoices.FirstOrDefaultAsync(new CarrierInvoiceByNumberSpec(query.CarrierId, query.InvoiceNumber), cancellationToken);
        if (invoice is null)
        {
            return CarrierInvoiceRefused.Because<AgentInvoiceLineView>(CarrierInvoiceRefusal.InvoiceNotFound(query.CarrierId.Value, query.InvoiceNumber));
        }

        var line = invoice.Lines.FirstOrDefault(candidate => candidate.CarrierBookingReference == query.CarrierBookingReference);
        if (line is null)
        {
            return CarrierInvoiceRefused.Because<AgentInvoiceLineView>(CarrierInvoiceRefusal.LineNotFound(query.InvoiceNumber, query.CarrierBookingReference));
        }

        var carrier = await carriers.FirstOrDefaultAsync(new CarrierByIdSpec(invoice.CarrierId), cancellationToken);
        var cost = line.CarrierBookingNumber is { } number
            ? await costs.FirstOrDefaultAsync(new CarrierCostByBookingNumberSpec(number), cancellationToken)
            : null;

        return new AgentInvoiceLineView(
            invoice.CarrierId.Value,
            carrier?.Name ?? invoice.CarrierId.Value,
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            line.CarrierBookingReference,
            line.Result.ToString(),
            line.Status.ToString(),
            line.Amount,
            CarrierCostCharge.Currency,
            tolerance.Amount,
            cost is null ? null : line.Amount - cost.Total,
            cost is null ? null : CostOf(cost),
            line.Result == CarrierInvoiceLineResult.Duplicate && cost is not null
                ? await EarlierLineAsync(invoice, line, cost, cancellationToken)
                : null,
            line.Result == CarrierInvoiceLineResult.UnknownBooking ? await CandidatesAsync(invoice, line, cancellationToken) : [],
            line.ResolvesAutomatically,
            AgentActionNames.Of(invoice.AllowedActionsOf(line.CarrierBookingReference, cost)));
    }

    // The label is all the Agent gets of a Spot price: the Planner's remark is free text and stays in the TMS and the ERP.
    private static AgentCarrierCostView CostOf(CarrierCost cost) => new(
        cost.Id.CarrierBookingNumber,
        cost.CarrierBookingReference,
        cost.Total,
        cost.Source.Label,
        cost.DeliveredOn,
        cost.Status.ToString());

    private async Task<AgentEarlierLineView?> EarlierLineAsync(CarrierInvoice invoice, CarrierInvoiceLine line, CarrierCost cost, CancellationToken cancellationToken)
    {
        var all = await invoices.ListAsync(new CarrierInvoicesOfCarrierSpec(invoice.CarrierId), cancellationToken);
        return EarlierLine.Find(all, cost.Id.CarrierBookingNumber, invoice.InvoiceNumber, line.CarrierBookingReference) is { } earlier
            ? new AgentEarlierLineView(earlier.Invoice.InvoiceNumber, earlier.Invoice.InvoiceDate, earlier.Line.CarrierBookingReference, earlier.Line.Status.ToString(), earlier.Line.AcceptedAmount ?? earlier.Line.Amount)
            : null;
    }

    private async Task<IReadOnlyList<AgentCandidateView>> CandidatesAsync(CarrierInvoice invoice, CarrierInvoiceLine line, CancellationToken cancellationToken)
    {
        var open = await costs.ListAsync(new OpenCarrierCostsOfCarrierSpec(invoice.CarrierId), cancellationToken);
        return
        [
            .. CandidateCarrierCosts.Rank(line.CarrierBookingReference, line.Amount, invoice.InvoiceDate, open)
                .Select((candidate, index) => new AgentCandidateView(
                    index + 1,
                    candidate.Cost.Id.CarrierBookingNumber,
                    candidate.Cost.CarrierBookingReference,
                    candidate.Cost.Total,
                    line.Amount - candidate.Cost.Total,
                    candidate.Cost.Source.Label,
                    candidate.Cost.DeliveredOn)),
        ];
    }
}
