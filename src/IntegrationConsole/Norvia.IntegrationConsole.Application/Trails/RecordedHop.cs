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
/// <param name="Fault">The name of the runtime fault that made the hop fail, such as <c>driver-app-unavailable</c>, or null when no fault did.</param>
/// <param name="AgentId">The Agent that made the hop, such as a tool call, or null when none did (ADR 0040).</param>
/// <param name="ToolName">The MCP tool the Agent called, or null when the hop wasn't a tool call.</param>
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
    string? MessageId = null,
    string? Fault = null,
    string? AgentId = null,
    string? ToolName = null);
