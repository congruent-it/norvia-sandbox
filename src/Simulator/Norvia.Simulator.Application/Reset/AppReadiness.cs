namespace Norvia.Simulator.Application.Reset;

/// <summary>What one application said when asked whether it would take a reset.</summary>
/// <param name="Reachable">False when the application couldn't be asked: it didn't answer, or the Simulator isn't set up to reach its reset door.</param>
/// <param name="Enabled">Whether the estate switched the application's Reset setting on.</param>
/// <param name="BusySubscription">The first subscription holding unprocessed messages, as <c>topic/subscription</c>, or null.</param>
public sealed record AppReadiness(EstateApp App, bool Reachable, bool Enabled, string? BusySubscription);
