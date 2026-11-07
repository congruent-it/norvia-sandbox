using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Explain;

namespace Norvia.Tms.Web.Agents;

/// <summary>Sends the Explain queries in a scope of their own, as the other requests do (a circuit lives as long as the page is open).</summary>
internal sealed class ExplainRequests(IServiceScopeFactory scopes) : IExplainRequests
{
    public Task<Result<Suggestion>> ExplainAsync(ExplainRejectedBookingQuery query, CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    public Task<Result<Suggestion>> ExplainAsync(ExplainTransportOrderQuery query, CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    public Task<Result<Suggestion>> ExplainAsync(ExplainRatingFailureQuery query, CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    public Task<Result<Suggestion>> ExplainAsync(ExplainNotYetBillableQuery query, CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    private async Task<Result<Suggestion>> SendAsync(IQuery<Result<Suggestion>> query, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, cancellationToken);
    }
}
