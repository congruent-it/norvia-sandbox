using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.Runs;

/// <summary>
/// Where a Logic App run is read as it is now: the Logic Apps Standard management API, fetched live (ADR 0017). It reads
/// as the console's own identity, never with the operator's token, and it never resubmits a run.
/// </summary>
public interface IWorkflowRuns
{
    /// <summary>The run and its steps, without their bodies, or null once run history no longer keeps the run.</summary>
    Task<RecordedRun?> RunAsync(WorkflowRun run, CancellationToken cancellationToken);

    /// <summary>
    /// The input and output bodies of one step of the run, or null once run history no longer keeps the run or the step.
    /// A body behind secure inputs or outputs is not fetched.
    /// </summary>
    Task<RecordedBodies?> BodiesAsync(WorkflowRun run, string stepName, CancellationToken cancellationToken);
}
