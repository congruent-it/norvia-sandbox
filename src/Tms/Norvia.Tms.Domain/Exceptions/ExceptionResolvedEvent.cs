using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>The Planner resolved an Exception (P7).</summary>
public sealed class ExceptionResolvedEvent(ExceptionCase exception) : DomainEventBase
{
    public ExceptionCase Exception { get; } = exception;
}
