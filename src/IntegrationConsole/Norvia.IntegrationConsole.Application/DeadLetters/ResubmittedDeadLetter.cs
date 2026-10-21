using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.DeadLetters;

/// <summary>A dead-lettered message as it was resubmitted: the original, and the copy sent in its place.</summary>
/// <param name="Entity">The queue it was resubmitted to, or the subscription as <c>topic/subscription</c>.</param>
/// <param name="OriginalMessageId">The message ID of the dead-lettered original, which the copy names in <c>resubmittedFrom</c>.</param>
/// <param name="ResubmittedMessageId">The new message ID of the copy.</param>
/// <param name="CorrelationId">The original's correlation ID, which the copy keeps so the trail continues.</param>
public sealed record ResubmittedDeadLetter(
    string Entity,
    string OriginalMessageId,
    string ResubmittedMessageId,
    string? CorrelationId,
    IReadOnlyList<BusinessKey> BusinessKeys);
