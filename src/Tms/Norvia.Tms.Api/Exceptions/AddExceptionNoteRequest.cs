namespace Norvia.Tms.Api.Exceptions;

/// <summary>A Planner's note on an Exception: the free text, and who wrote it.</summary>
/// <param name="Text">Required: for example "phoned Kessler, redelivery Thursday".</param>
/// <param name="Planner">Required: the Planner's name, as the TMS UI names them.</param>
public sealed record AddExceptionNoteRequest(string? Text, string? Planner);
