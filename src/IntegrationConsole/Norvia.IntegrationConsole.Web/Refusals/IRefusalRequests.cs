using Norvia.IntegrationConsole.Application.Refusals.List;

namespace Norvia.IntegrationConsole.Web.Refusals;

/// <summary>What the refused-at-the-edge page asks of the application.</summary>
public interface IRefusalRequests
{
    Task<RefusedCallList> ListAsync(ListRefusedCallsQuery query, CancellationToken cancellationToken);
}
