using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.Runs;

/// <summary>A run that failed as it is now, with what it tracked (the tracking contract).</summary>
/// <param name="Run">The Logic App run, or null where there are no Logic Apps to open it in, as locally.</param>
/// <param name="Flow">The flow the run tracked, or null when none of its steps did.</param>
/// <param name="CorrelationId">The Message trail's correlation ID the run tracked, or null when none of its steps did.</param>
/// <param name="FailedStep">The step that failed first, or null when the source shows none.</param>
public sealed record RecordedFailedRun(
    WorkflowRun? Run,
    DateTimeOffset Start,
    string? Flow,
    string? CorrelationId,
    string? FailedStep,
    StepError? Error);
