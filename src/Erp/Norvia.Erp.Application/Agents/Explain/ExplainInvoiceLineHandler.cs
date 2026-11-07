using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Erp.Application.CarrierInvoices.AgentView;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Application.Agents.Explain;

public sealed class ExplainInvoiceLineHandler(IReadRepository<CarrierInvoice> invoices, IReadRepository<CarrierCost> costs, IAgentClient agent)
    : IQueryHandler<ExplainInvoiceLineQuery, Result<Suggestion>>
{
    /// <summary>The Carrier invoice matching agent's definition (agents/finance-matching.agent.yaml).</summary>
    public const string AgentName = "finance-matching";

    public async ValueTask<Result<Suggestion>> Handle(ExplainInvoiceLineQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoice = await invoices.FirstOrDefaultAsync(new CarrierInvoiceByNumberSpec(query.CarrierId, query.InvoiceNumber), cancellationToken);
        var line = invoice?.Lines.FirstOrDefault(candidate => candidate.CarrierBookingReference == query.CarrierBookingReference);
        if (invoice is null || line is null)
        {
            return Result.NotFound();
        }

        // Only a line that waits for Finance has something to explain; one that is decided or matched has nothing left to say.
        if (line.Status != CarrierInvoiceLineStatus.ToDecide)
        {
            return Result.Invalid(new ValidationError("Only a Carrier invoice line that is To decide can be explained."));
        }

        // The Carrier typed the invoice number and the reference. Only a plain identifier is put in the question and passed to a tool.
        if (!FinanceQuestions.IsPlainIdentifier(query.InvoiceNumber) || !FinanceQuestions.IsPlainIdentifier(query.CarrierBookingReference) || !FinanceQuestions.IsPlainIdentifier(query.CarrierId.Value))
        {
            return Result.Invalid(new ValidationError("The invoice number and the reference of this line are not plain identifiers, so the line cannot be explained."));
        }

        // The invoice's own correlation ID, so the Agent's tool calls join its Message trail. The same question every time: Finance asks
        // nothing free-form of the Agent.
        var answer = await agent.AskAsync(
            new AgentRequest(AgentName, FinanceQuestions.ExplainInvoiceLine(query.CarrierId.Value, query.InvoiceNumber, query.CarrierBookingReference), invoice.CorrelationId),
            cancellationToken);
        if (!answer.IsSuccess)
        {
            return answer;
        }

        // The ERP decides what Finance may do with the line, never the Agent: the same rules Accept and Reject enforce.
        var cost = line.CarrierBookingNumber is { } number
            ? await costs.FirstOrDefaultAsync(new CarrierCostByBookingNumberSpec(number), cancellationToken)
            : null;
        var allowed = invoice.AllowedActionsOf(line.CarrierBookingReference, cost);
        return AgentActionNames.Allows(allowed, answer.Value.Action)
            ? answer
            : Result.Success(answer.Value with
            {
                Action = SuggestedActionKind.NeedsAPerson,
                SuggestedAction = $"No action is suggested: the assistant proposed '{answer.Value.Action}', which is not allowed for this line. Decide it yourself.",
            });
    }
}
