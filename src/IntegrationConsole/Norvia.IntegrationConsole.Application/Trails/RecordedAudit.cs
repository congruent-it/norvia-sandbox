namespace Norvia.IntegrationConsole.Application.Trails;

/// <summary>
/// An audit event the console wrote when an operator resubmitted or discarded a message, as the <see cref="ITrailSource"/>
/// recorded it under the trail's correlation ID.
/// </summary>
/// <param name="Actor">Who did it.</param>
/// <param name="Action">What they did, as <see cref="Audit.AuditActions"/> names it.</param>
/// <param name="Entity">The queue or subscription the message was on.</param>
/// <param name="MessageId">The ID of the dead-lettered message it was done to.</param>
/// <param name="At">When.</param>
/// <param name="Reason">Why, for a discard.</param>
public sealed record RecordedAudit(
    string Actor,
    string Action,
    string Entity,
    string MessageId,
    DateTimeOffset At,
    string? Reason,
    IReadOnlyList<BusinessKey> BusinessKeys);
