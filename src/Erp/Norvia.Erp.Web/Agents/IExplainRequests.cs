using Ardalis.Result;
using Norvia.Agents;
using Norvia.Erp.Application.Agents.Explain;
using Norvia.Erp.Application.Agents.RejectionReason;

namespace Norvia.Erp.Web.Agents;

/// <summary>What the Explain button asks of the application.</summary>
public interface IExplainRequests
{
    Task<Result<Suggestion>> ExplainAsync(ExplainInvoiceLineQuery query, CancellationToken cancellationToken);

    Task<Result<Suggestion>> ExplainAsync(ExplainInvoiceQuery query, CancellationToken cancellationToken);

    /// <summary>What the Draft reason button of the Reject dialog asks: an editable, Carrier-facing reason from the rejection reason agent.</summary>
    Task<Result<Suggestion>> DraftRejectionReasonAsync(DraftRejectionReasonQuery query, CancellationToken cancellationToken);
}
