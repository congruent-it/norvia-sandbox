using Ardalis.Result;
using Mediator;

namespace Norvia.IntegrationConsole.Application.Trails.Open;

/// <summary>
/// One Message trail: everything recorded under its correlation ID, fan-out included, as far back as the estate keeps
/// anything.
/// </summary>
public sealed record OpenTrailQuery(string CorrelationId) : IQuery<Result<MessageTrail>>;
