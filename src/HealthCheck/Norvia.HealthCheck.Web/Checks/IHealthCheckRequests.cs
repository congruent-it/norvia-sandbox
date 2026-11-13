using Ardalis.Result;
using Norvia.Agents;
using Norvia.HealthCheck.Application.Cost;
using Norvia.HealthCheck.Application.Narrate;
using Norvia.HealthCheck.Application.Run;

namespace Norvia.HealthCheck.Web.Checks;

/// <summary>What the page asks of the application.</summary>
public interface IHealthCheckRequests
{
    Task<Result<HealthCheckReport>> RunAsync(RunHealthCheckQuery query, CancellationToken cancellationToken);

    Task<Result<Narration>> NarrateAsync(NarrateHealthCheckQuery query, CancellationToken cancellationToken);

    Task<Result<NarratedItem>> ExplainAsync(ExplainFindingQuery query, CancellationToken cancellationToken);

    Task<Result<CostView>> CostAsync(GetCostViewQuery query, CancellationToken cancellationToken);
}
