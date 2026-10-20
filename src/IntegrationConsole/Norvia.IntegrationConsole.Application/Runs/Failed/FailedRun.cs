using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.Runs.Failed;

/// <summary>One failed run, view only: there is nothing to resubmit (docs/operations/integration-console.md).</summary>
/// <param name="Flow">The flow it tracked, or its workflow's name when it tracked none.</param>
/// <param name="CorrelationId">Opens the Message trail, or null when the run tracked none.</param>
public sealed record FailedRun(string Flow, WorkflowRun Run, DateTimeOffset Start, string? CorrelationId, string? FailedStep, StepError? Error);
