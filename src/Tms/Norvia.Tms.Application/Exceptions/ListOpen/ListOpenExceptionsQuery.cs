using Mediator;

namespace Norvia.Tms.Application.Exceptions.ListOpen;

/// <summary>The Planner's list of Open Exceptions (P7), oldest first.</summary>
public sealed record ListOpenExceptionsQuery : IQuery<IReadOnlyList<OpenExceptionSummary>>;
