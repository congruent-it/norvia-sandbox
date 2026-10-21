using Mediator;
using Norvia.IntegrationConsole.Application.DeadLetters.List;

namespace Norvia.IntegrationConsole.Web.DeadLetters;

/// <summary>Sends the application's queries, each in a scope of its own (see RunRequests).</summary>
internal sealed class DeadLetterRequests(IServiceScopeFactory scopes) : IDeadLetterRequests
{
    public async Task<DeadLetterQueueList> ListAsync(ListDeadLetterQueuesQuery query, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, cancellationToken);
    }
}
