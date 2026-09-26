using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.Runs.Open;

public sealed class OpenRunHandler(IWorkflowRuns runs, IOptions<IntegrationRetention> retention)
    : IQueryHandler<OpenRunQuery, Result<RunDetail>>
{
    public async ValueTask<Result<RunDetail>> Handle(OpenRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (RunOf(query.LogicApp, query.Workflow, query.RunId) is not { } run)
        {
            return Result.Invalid(new ValidationError(nameof(query.RunId), "Open a run by its Logic App, workflow and run ID."));
        }

        var days = retention.Value.RunHistoryDays;
        if (await runs.RunAsync(run, cancellationToken) is not { } recorded)
        {
            return new RunDetail(run, IsKept: false, null, null, null, [], days);
        }

        // A step that never ran, such as one skipped after a failure, has no start and comes last.
        var steps = recorded.Steps
            .OrderBy(step => step.Start ?? DateTimeOffset.MaxValue)
            .Select(Step)
            .ToList();

        return new RunDetail(run, IsKept: true, recorded.Status, recorded.Start, Between(recorded.Start, recorded.End), steps, days);
    }

    /// <summary>The run the operator opened, or null when a part of it is missing.</summary>
    internal static WorkflowRun? RunOf(string? logicApp, string? workflow, string? runId) =>
        logicApp?.Trim() is { Length: > 0 } app && workflow?.Trim() is { Length: > 0 } flow && runId?.Trim() is { Length: > 0 } id
            ? new WorkflowRun(app, flow, id)
            : null;

    private static RunStep Step(RecordedStep step) =>
        new(step.Name, step.Status, step.Start, Between(step.Start, step.End), step.Error, step.SecureInputs, step.SecureOutputs);

    private static TimeSpan? Between(DateTimeOffset? start, DateTimeOffset? end) =>
        start is { } from && end is { } to && to >= from ? to - from : null;
}
