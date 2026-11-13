using Mediator;

namespace Norvia.IntegrationConsole.Application.Refusals.List;

/// <summary>The calls refused at the edge in the last 24 hours, newest first, narrowed to a status and an operation when given.</summary>
public sealed record ListRefusedCallsQuery(int? Status = null, string? Operation = null) : IQuery<RefusedCallList>;
