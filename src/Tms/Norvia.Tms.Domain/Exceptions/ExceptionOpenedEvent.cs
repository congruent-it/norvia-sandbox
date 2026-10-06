using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>The TMS opened an Exception (P7), for the Tracking event or Proof of delivery that caused it.</summary>
public sealed class ExceptionOpenedEvent(ExceptionCase exception, ExceptionCause cause) : DomainEventBase
{
    public ExceptionCase Exception { get; } = exception;

    public ExceptionCause Cause { get; } = cause;
}
