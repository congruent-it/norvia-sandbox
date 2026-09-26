namespace Norvia.IntegrationConsole.Application.Runs;

/// <summary>One step of a Logic App run, its trigger or an action, as run history has it, without its bodies.</summary>
/// <param name="Name">The step's name in the workflow, unique in it.</param>
/// <param name="Status">As Logic Apps names it, such as <c>Succeeded</c>, <c>Failed</c> or <c>Skipped</c>.</param>
/// <param name="SecureInputs">Whether the step uses secure inputs, so run history doesn't show them.</param>
/// <param name="SecureOutputs">Whether the step uses secure outputs, so run history doesn't show them.</param>
public sealed record RecordedStep(
    string Name,
    string Status,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    StepError? Error,
    bool SecureInputs,
    bool SecureOutputs);
