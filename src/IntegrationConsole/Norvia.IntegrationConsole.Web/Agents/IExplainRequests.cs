using Ardalis.Result;
using Norvia.Agents;
using Norvia.IntegrationConsole.Application.Agents.Explain;

namespace Norvia.IntegrationConsole.Web.Agents;

/// <summary>What the Explain button asks of the application.</summary>
public interface IExplainRequests
{
    Task<Result<Suggestion>> ExplainAsync(ExplainTrailQuery query, CancellationToken cancellationToken);

    Task<Result<Suggestion>> ExplainAsync(ExplainFailedRunQuery query, CancellationToken cancellationToken);

    Task<Result<Suggestion>> ExplainAsync(ExplainDeadLetterQuery query, CancellationToken cancellationToken);

    Task<Result<Suggestion>> SummariseAsync(SummariseDeadLettersQuery query, CancellationToken cancellationToken);
}
