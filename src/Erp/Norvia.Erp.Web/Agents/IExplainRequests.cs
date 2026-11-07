using Ardalis.Result;
using Norvia.Agents;
using Norvia.Erp.Application.Agents.Explain;

namespace Norvia.Erp.Web.Agents;

/// <summary>What the Explain button asks of the application.</summary>
public interface IExplainRequests
{
    Task<Result<Suggestion>> ExplainAsync(ExplainInvoiceLineQuery query, CancellationToken cancellationToken);
}
