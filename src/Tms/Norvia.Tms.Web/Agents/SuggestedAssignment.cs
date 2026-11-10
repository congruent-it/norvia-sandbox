using Norvia.Agents;

namespace Norvia.Tms.Web.Agents;

/// <summary>
/// A Suggestion of the Planner agent that pre-selects who carries a Transport Order in the assign flow. It executes nothing: the Planner
/// confirms with the same button as ever. The assignment names the Agent's run (ADR 0040) only when the Planner then assigns that order to
/// who was suggested; a Suggestion the Planner ignores or overrides is recorded nowhere.
/// </summary>
/// <param name="CarrierId">The suggested Carrier; null for Own fleet.</param>
public sealed record SuggestedAssignment(string TransportOrderNumber, string? CarrierId, AgentRun Run)
{
    /// <summary>The pre-selection a Suggestion makes, or null when it suggests no assignment or has no run to name. Needs a person pre-selects nothing.</summary>
    public static SuggestedAssignment? From(string transportOrderNumber, Suggestion suggestion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transportOrderNumber);
        ArgumentNullException.ThrowIfNull(suggestion);
        return suggestion switch
        {
            { Run: { } run, Action: SuggestedActionKind.AssignOwnFleet } => new SuggestedAssignment(transportOrderNumber, null, run),
            { Run: { } run, Action: SuggestedActionKind.AssignCarrier, CarrierId: { Length: > 0 } carrierId } => new SuggestedAssignment(transportOrderNumber, carrierId, run),
            _ => null,
        };
    }

    /// <summary>
    /// The run to name in the trace of assigning <paramref name="transportOrderNumber"/> to <paramref name="carrierId"/> (null for Own
    /// fleet): the Suggestion's own when the Planner did what it suggested, none otherwise.
    /// </summary>
    public AgentRun? RunFor(string transportOrderNumber, string? carrierId) =>
        transportOrderNumber == TransportOrderNumber && carrierId == CarrierId ? Run : null;
}
