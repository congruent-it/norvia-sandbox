using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Exceptions.Resolve;

/// <summary>
/// The Planner resolves an open Exception with an outcome and a remark (P7), and the TMS publishes ExceptionResolved. The
/// outcome and remark are checked at the boundary: the remark is required. The action starts in the TMS UI, so its
/// correlation ID is the TMS's own.
/// </summary>
public sealed record ResolveExceptionCommand(string ExceptionNumber, ExceptionOutcome Outcome, string Remark, string CorrelationId)
    : ICommand<Result<ResolveExceptionResult>>;
