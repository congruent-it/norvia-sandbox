namespace Norvia.IntegrationConsole.Application.Trails;

/// <summary>
/// One hop of a Message trail as the <see cref="ITrailSource"/> recorded it, reduced to the tracking contract's keys.
/// </summary>
/// <param name="Id">The hop's own ID in the telemetry, unique on its trail.</param>
/// <param name="CausedBy">The <see cref="Id"/> of the hop on the same trail that started this one, or null when none did.</param>
/// <param name="Name">What the hop was, as the telemetry names it, such as an application call's route.</param>
/// <param name="Duration">How long it took, or null when the telemetry recorded only a moment.</param>
/// <param name="Run">The Logic App run the hop was, or null when it wasn't one.</param>
/// <param name="MessageId">The Service Bus message ID of the message the hop dead-lettered, or null when it recorded none.</param>
public sealed record RecordedHop(
    string Id,
    string? CausedBy,
    string Name,
    string? Flow,
    DateTimeOffset Time,
    TimeSpan? Duration,
    Outcome? Outcome,
    string? FailureCode,
    IReadOnlyList<BusinessKey> BusinessKeys,
    WorkflowRun? Run = null,
    string? MessageId = null);
