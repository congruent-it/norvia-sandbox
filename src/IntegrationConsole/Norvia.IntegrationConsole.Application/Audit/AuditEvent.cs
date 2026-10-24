using Norvia.Agents;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.Audit;

/// <summary>What an operator did to a message in the console, for the audit (docs/operations/integration-console.md).</summary>
/// <param name="Actor">Who, from the operator's token.</param>
/// <param name="Action">What they did, such as <see cref="AuditActions.Resubmit"/>.</param>
/// <param name="Entity">The queue or subscription the message was on.</param>
/// <param name="MessageId">The message's ID.</param>
/// <param name="CorrelationId">The message's correlation ID, which puts the event on its Message trail.</param>
/// <param name="BusinessKeys">The tracking contract's keys the message carried.</param>
/// <param name="At">When.</param>
/// <param name="Reason">Why, for a discard, which needs one. Null for a resubmit.</param>
/// <param name="Agent">The Agent run whose Suggestion the operator acted on, or null when they acted on their own (ADR 0040).</param>
public sealed record AuditEvent(
    string Actor,
    string Action,
    string Entity,
    string MessageId,
    string? CorrelationId,
    IReadOnlyList<BusinessKey> BusinessKeys,
    DateTimeOffset At,
    string? Reason = null,
    AgentRun? Agent = null);
