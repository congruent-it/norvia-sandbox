using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Application.Agents.Explain;

public sealed class ExplainInvoiceHandler(IReadRepository<CarrierInvoice> invoices, IAgentClient agent)
    : IQueryHandler<ExplainInvoiceQuery, Result<Suggestion>>
{
    public async ValueTask<Result<Suggestion>> Handle(ExplainInvoiceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoice = await invoices.FirstOrDefaultAsync(new CarrierInvoiceByNumberSpec(query.CarrierId, query.InvoiceNumber), cancellationToken);
        if (invoice is null)
        {
            return Result.NotFound();
        }

        // An Approved invoice has no line left To decide, so there is nothing to explain.
        if (invoice.Status != CarrierInvoiceStatus.Received)
        {
            return Result.Invalid(new ValidationError("Only a Carrier invoice with lines To decide can be explained."));
        }

        // The Carrier typed the invoice number. Only a plain identifier is put in the question and passed to a tool.
        if (!FinanceQuestions.IsPlainIdentifier(query.InvoiceNumber) || !FinanceQuestions.IsPlainIdentifier(query.CarrierId.Value))
        {
            return Result.Invalid(new ValidationError("The invoice number of this Carrier invoice is not a plain identifier, so the invoice cannot be explained."));
        }

        return await agent.AskAsync(
            new AgentRequest(ExplainInvoiceLineHandler.AgentName, FinanceQuestions.ExplainInvoice(query.CarrierId.Value, query.InvoiceNumber), invoice.CorrelationId),
            cancellationToken);
    }
}
