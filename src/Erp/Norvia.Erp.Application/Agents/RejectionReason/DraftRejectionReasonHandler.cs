using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Application.Agents.RejectionReason;

/// <summary>
/// Asks the rejection reason agent for a draft. The agent is a separate definition and identity with one tool, the Carrier-visible facts
/// of the line (ADR 0040), so it cannot name the Planner's remark, a Rate Card version or another Carrier's reference. The draft arrives
/// as the Suggestion's cause; nothing is sent to the Carrier except by Finance's confirmed rejection.
/// </summary>
public sealed class DraftRejectionReasonHandler(IReadRepository<CarrierInvoice> invoices, IAgentClient agent)
    : IQueryHandler<DraftRejectionReasonQuery, Result<Suggestion>>
{
    /// <summary>The rejection reason agent's definition (agents/finance-rejection-reason.agent.yaml).</summary>
    public const string AgentName = "finance-rejection-reason";

    public async ValueTask<Result<Suggestion>> Handle(DraftRejectionReasonQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoice = await invoices.FirstOrDefaultAsync(new CarrierInvoiceByNumberSpec(query.CarrierId, query.InvoiceNumber), cancellationToken);
        var line = invoice?.Lines.FirstOrDefault(candidate => candidate.CarrierBookingReference == query.CarrierBookingReference);
        if (invoice is null || line is null)
        {
            return Result.NotFound();
        }

        // Only a line that waits for Finance can still be rejected.
        if (line.Status != CarrierInvoiceLineStatus.ToDecide)
        {
            return Result.Invalid(new ValidationError("Only a Carrier invoice line that is To decide can be rejected, so only its reason can be drafted."));
        }

        // The Carrier typed the invoice number and the reference. Only a plain identifier is put in the question and passed to the tool.
        if (!FinanceQuestions.IsPlainIdentifier(query.InvoiceNumber) || !FinanceQuestions.IsPlainIdentifier(query.CarrierBookingReference) || !FinanceQuestions.IsPlainIdentifier(query.CarrierId.Value))
        {
            return Result.Invalid(new ValidationError("The invoice number and the reference of this line are not plain identifiers, so its reason cannot be drafted."));
        }

        // The invoice's own correlation ID, so the Agent's tool call joins its Message trail. The same question every time.
        return await agent.AskAsync(
            new AgentRequest(AgentName, FinanceQuestions.DraftRejectionReason(query.CarrierId.Value, query.InvoiceNumber, query.CarrierBookingReference), invoice.CorrelationId),
            cancellationToken);
    }
}
