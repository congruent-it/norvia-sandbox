using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Explain;

namespace Norvia.Tms.Web.Agents;

/// <summary>Sends the Explain query in a scope of its own, as the other requests do (a circuit lives as long as the page is open).</summary>
internal sealed class ExplainRequests(IServiceScopeFactory scopes) : IExplainRequests
{
    public async Task<Result<Suggestion>> ExplainAsync(ExplainRejectedBookingQuery query, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, cancellationToken);
    }
}
