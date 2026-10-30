namespace Norvia.HealthCheck.Application.Run;

/// <summary>
/// The last report this app made, kept in memory only, so the Agent's run of the Checks need not read the estate again straight after the page's.
/// The estate is the same whoever asks (the app reads it with its own identity), so the report is not tied to a user.
/// </summary>
public sealed class RecentReport
{
    /// <summary>Held while a run that may reuse the report decides to, so two such runs at once read the estate once.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public HealthCheckReport? Last { get; set; }
}
