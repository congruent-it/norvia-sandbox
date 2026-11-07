using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Erp.Application.Agents.Explain;
using Norvia.Erp.Application.Agents.RejectionReason;

namespace Norvia.Erp.Web.Agents;

/// <summary>Sends the Explain query in a scope of its own, as the other requests do (a circuit lives as long as the page is open).</summary>
internal sealed class ExplainRequests(IServiceScopeFactory scopes) : IExplainRequests
{
    public Task<Result<Suggestion>> ExplainAsync(ExplainInvoiceLineQuery query, CancellationToken cancellationToken) => SendAsync(query, cancellationToken);

    public Task<Result<Suggestion>> ExplainAsync(ExplainInvoiceQuery query, CancellationToken cancellationToken) => SendAsync(query, cancellationToken);

    public Task<Result<Suggestion>> DraftRejectionReasonAsync(DraftRejectionReasonQuery query, CancellationToken cancellationToken) => SendAsync(query, cancellationToken);

    private async Task<Result<Suggestion>> SendAsync(IQuery<Result<Suggestion>> query, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, cancellationToken);
    }
}
