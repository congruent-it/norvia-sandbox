namespace Norvia.Tms.Api.Exceptions;

/// <summary>A Planner's note on an Exception: the free text, and who wrote it.</summary>
/// <param name="Text">Required: for example "phoned Kessler, redelivery Thursday".</param>
/// <param name="Planner">Required: the Planner's name, as the TMS UI names them.</param>
/// <param name="AgentName">Optional, with <paramref name="AgentRunId"/>: the Agent whose draft the note was saved from, edited or not, which the note's trace then names.</param>
/// <param name="AgentRunId">Optional, with <paramref name="AgentName"/>: that Agent's run.</param>
public sealed record AddExceptionNoteRequest(string? Text, string? Planner, string? AgentName = null, string? AgentRunId = null);
