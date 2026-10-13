namespace Norvia.Simulator.Domain.Log;

/// <summary>Narrows a run's log: to an actor, an outcome, or lines that mention some text (a correlation ID, a reference or an order number).</summary>
public sealed record LogFilter(string? Actor = null, LogOutcome? Outcome = null, string? Text = null)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Actor) && Outcome is null && string.IsNullOrWhiteSpace(Text);
}
