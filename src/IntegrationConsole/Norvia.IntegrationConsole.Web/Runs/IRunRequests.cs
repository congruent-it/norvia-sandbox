using Ardalis.Result;
using Norvia.IntegrationConsole.Application.Runs.Failed;
using Norvia.IntegrationConsole.Application.Runs.Open;
using Norvia.IntegrationConsole.Application.Runs.Steps;

namespace Norvia.IntegrationConsole.Web.Runs;

/// <summary>What the run pages ask of the application.</summary>
public interface IRunRequests
{
    Task<Result<RunDetail>> OpenAsync(OpenRunQuery query, CancellationToken cancellationToken);

    Task<Result<StepDetail>> OpenStepAsync(OpenStepQuery query, CancellationToken cancellationToken);

    Task<FailedRunList> ListFailedAsync(ListFailedRunsQuery query, CancellationToken cancellationToken);
}
