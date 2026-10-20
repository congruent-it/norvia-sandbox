using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.Runs;

/// <summary>A Logic App run that failed, as run history has it now, with what its steps tracked (the tracking contract).</summary>
/// <param name="Flow">The flow the run tracked, or null when none of its steps did.</param>
/// <param name="CorrelationId">The Message trail's correlation ID the run tracked, or null when none of its steps did.</param>
/// <param name="FailedStep">The step that failed first, or null when run history shows none.</param>
public sealed record RecordedFailedRun(
    WorkflowRun Run,
    DateTimeOffset Start,
    string? Flow,
    string? CorrelationId,
    string? FailedStep,
    StepError? Error);
