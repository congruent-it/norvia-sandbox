using Ardalis.Result;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Explain;

namespace Norvia.Tms.Web.Agents;

/// <summary>What the Explain buttons ask of the application.</summary>
public interface IExplainRequests
{
    Task<Result<Suggestion>> ExplainAsync(ExplainRejectedBookingQuery query, CancellationToken cancellationToken);

    Task<Result<Suggestion>> ExplainAsync(ExplainTransportOrderQuery query, CancellationToken cancellationToken);
}
