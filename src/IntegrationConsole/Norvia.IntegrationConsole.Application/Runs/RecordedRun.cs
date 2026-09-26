namespace Norvia.IntegrationConsole.Application.Runs;

/// <summary>A Logic App run as run history has it now: its status, when it ran, and its trigger and actions.</summary>
/// <param name="Status">As Logic Apps names it, such as <c>Succeeded</c>, <c>Failed</c> or <c>Running</c>.</param>
/// <param name="End">When it ended, or null while it's still running.</param>
/// <param name="Steps">Its trigger and actions, in no particular order.</param>
public sealed record RecordedRun(string Status, DateTimeOffset? Start, DateTimeOffset? End, IReadOnlyList<RecordedStep> Steps);
