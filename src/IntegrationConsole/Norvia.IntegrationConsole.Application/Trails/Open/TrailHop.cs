namespace Norvia.IntegrationConsole.Application.Trails.Open;

/// <summary>One hop on the trail page.</summary>
/// <param name="Depth">How far it is indented: one level deeper for each fan-out above it.</param>
/// <param name="Run">The Logic App run the hop was, which opens its run detail, or null when it wasn't one.</param>
/// <param name="DeadLetter">What became of the message a dead-lettered hop left behind, or null when it isn't one or its message isn't known.</param>
/// <param name="Fault">The runtime fault that made the hop fail, such as <c>driver-app-unavailable</c>, so a demo shows cause and effect, or null when none did.</param>
public sealed record TrailHop(
    string Name,
    string? Flow,
    DateTimeOffset Time,
    TimeSpan? Duration,
    Outcome? Outcome,
    string? FailureCode,
    int Depth,
    WorkflowRun? Run,
    DeadLetterHandling? DeadLetter = null,
    string? Fault = null)
{
    /// <summary>Whether the integration layer failed the message here, as a trail's failure is counted in search.</summary>
    public bool HasFailure => Outcome is Trails.Outcome.DeadLettered or Trails.Outcome.Failed;
}
