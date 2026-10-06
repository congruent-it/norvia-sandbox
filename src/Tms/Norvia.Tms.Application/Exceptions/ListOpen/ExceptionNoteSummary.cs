namespace Norvia.Tms.Application.Exceptions.ListOpen;

/// <summary>A Planner's note on an Exception, as the Planner's list of Open Exceptions shows it (P7).</summary>
public sealed record ExceptionNoteSummary(string Text, string Planner, DateTimeOffset AddedAt, string CorrelationId);
