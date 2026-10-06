using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Exceptions.Resolve;

/// <summary>What resolving an Exception did.</summary>
/// <param name="CorrelationId">The resolution's own Message trail.</param>
public sealed record ResolveExceptionResult(
    string ExceptionNumber,
    string ShipmentNumber,
    ExceptionStatus Status,
    ExceptionOutcome Outcome,
    string CorrelationId);
