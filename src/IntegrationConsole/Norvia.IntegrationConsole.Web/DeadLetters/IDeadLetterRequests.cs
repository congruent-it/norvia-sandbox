using Norvia.IntegrationConsole.Application.DeadLetters.List;

namespace Norvia.IntegrationConsole.Web.DeadLetters;

/// <summary>What the dead-letter pages ask of the application.</summary>
public interface IDeadLetterRequests
{
    Task<DeadLetterQueueList> ListAsync(ListDeadLetterQueuesQuery query, CancellationToken cancellationToken);
}
