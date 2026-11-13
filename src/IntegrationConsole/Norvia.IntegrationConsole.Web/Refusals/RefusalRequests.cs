using Mediator;
using Norvia.IntegrationConsole.Application.Refusals.List;

namespace Norvia.IntegrationConsole.Web.Refusals;

/// <summary>Sends the application's queries, each in a scope of its own (see RunRequests).</summary>
internal sealed class RefusalRequests(IServiceScopeFactory scopes) : IRefusalRequests
{
    public async Task<RefusedCallList> ListAsync(ListRefusedCallsQuery query, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, cancellationToken);
    }
}
