using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Runs.Open;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.Runs.Steps;

public sealed class OpenStepHandler(IWorkflowRuns runs, IOptions<IntegrationRetention> retention)
    : IQueryHandler<OpenStepQuery, Result<StepDetail>>
{
    public async ValueTask<Result<StepDetail>> Handle(OpenStepQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var step = query.Step?.Trim();
        if (OpenRunHandler.RunOf(query.LogicApp, query.Workflow, query.RunId) is not { } run || string.IsNullOrEmpty(step))
        {
            return Result.Invalid(new ValidationError(nameof(query.Step), "Open a step by its run and its name."));
        }

        var days = retention.Value.RunHistoryDays;
        if (await runs.BodiesAsync(run, step, cancellationToken) is not { } bodies)
        {
            return new StepDetail(run, step, IsKept: false, StepBody.None, StepBody.None, days);
        }

        return new StepDetail(run, step, IsKept: true, StepBody.Of(bodies.Input), StepBody.Of(bodies.Output), days);
    }
}
