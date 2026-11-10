using Ardalis.Result;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Explain;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Application.Agents.Reply;

namespace Norvia.Tms.Web.Agents;

/// <summary>What the Explain buttons ask of the application.</summary>
public interface IExplainRequests
{
    Task<Result<Suggestion>> ExplainAsync(ExplainRejectedBookingQuery query, CancellationToken cancellationToken);

    Task<Result<Suggestion>> ExplainAsync(ExplainTransportOrderQuery query, CancellationToken cancellationToken);

    Task<Result<Suggestion>> ExplainAsync(ExplainRatingFailureQuery query, CancellationToken cancellationToken);

    Task<Result<Suggestion>> ExplainAsync(ExplainNotYetBillableQuery query, CancellationToken cancellationToken);

    Task<Result<Suggestion>> RecommendAsync(RecommendReleaseQuery query, CancellationToken cancellationToken);

    /// <summary>A draft reply to the Shipper, as the Suggestion's cause. Nothing is sent or stored.</summary>
    Task<Result<Suggestion>> DraftAsync(DraftShipperReplyQuery query, CancellationToken cancellationToken);

    /// <summary>A recommendation of who should carry a Transport Order waiting for planning. Shown only: nothing is pre-selected or assigned.</summary>
    Task<Result<Suggestion>> SuggestAsync(SuggestAssignmentQuery query, CancellationToken cancellationToken);
}
