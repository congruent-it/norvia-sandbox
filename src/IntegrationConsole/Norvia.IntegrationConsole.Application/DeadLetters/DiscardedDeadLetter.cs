using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.DeadLetters;

/// <summary>A dead-lettered message as it was discarded.</summary>
/// <param name="Entity">The queue it was on, or the subscription as <c>topic/subscription</c>.</param>
/// <param name="MessageId">The discarded message's ID.</param>
/// <param name="CorrelationId">The message's correlation ID, which puts the audit event on its Message trail.</param>
public sealed record DiscardedDeadLetter(
    string Entity,
    string MessageId,
    string? CorrelationId,
    IReadOnlyList<BusinessKey> BusinessKeys);
