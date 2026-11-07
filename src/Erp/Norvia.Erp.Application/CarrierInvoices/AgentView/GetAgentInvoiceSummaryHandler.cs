using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierInvoices.AgentView;

/// <summary>
/// Builds the Finance agent's summary of a whole Carrier invoice: the ERP counts the lines by result and works out which may still
/// resolve, so the Agent explains and never counts (ADR 0040). Built field by field, and it holds no line's reference, description or
/// remark, nor a Carrier cost: what it does not name never reaches an Agent.
/// </summary>
public sealed class GetAgentInvoiceSummaryHandler(IReadRepository<CarrierInvoice> invoices, IReadRepository<Carrier> carriers)
    : IQueryHandler<GetAgentInvoiceSummaryQuery, Result<AgentInvoiceSummaryView>>
{
    private static readonly CarrierInvoiceLineResult[] ResultOrder = [.. Enum.GetValues<CarrierInvoiceLineResult>()];

    public async ValueTask<Result<AgentInvoiceSummaryView>> Handle(GetAgentInvoiceSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoice = await invoices.FirstOrDefaultAsync(new CarrierInvoiceByNumberSpec(query.CarrierId, query.InvoiceNumber), cancellationToken);
        if (invoice is null)
        {
            return CarrierInvoiceRefused.Because<AgentInvoiceSummaryView>(CarrierInvoiceRefusal.InvoiceNotFound(query.CarrierId.Value, query.InvoiceNumber));
        }

        var carrier = await carriers.FirstOrDefaultAsync(new CarrierByIdSpec(invoice.CarrierId), cancellationToken);
        var groups = ResultOrder
            .Select(result => Group(result, invoice.Lines.Where(line => line.Result == result).ToList()))
            .Where(group => group.Lines > 0)
            .ToList();

        return new AgentInvoiceSummaryView(
            invoice.CarrierId.Value,
            carrier?.Name ?? invoice.CarrierId.Value,
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            invoice.ReceivedAt,
            invoice.Status.ToString(),
            invoice.Total,
            CarrierCostCharge.Currency,
            invoice.Lines.Sum(line => line.AcceptedAmount ?? 0m),
            invoice.Lines.Count,
            groups.Sum(group => group.ToDecide),
            groups.Sum(group => group.ResolveAutomatically),
            groups);
    }

    private static AgentLineGroupView Group(CarrierInvoiceLineResult result, List<CarrierInvoiceLine> lines)
    {
        var toDecide = lines.Count(line => line.Status == CarrierInvoiceLineStatus.ToDecide);
        // The same rule as the line view: only an Unknown booking line that waits for Finance is matched again by itself.
        return new AgentLineGroupView(result.ToString(), lines.Count, toDecide, lines.Sum(line => line.Amount), result == CarrierInvoiceLineResult.UnknownBooking ? toDecide : 0);
    }
}
