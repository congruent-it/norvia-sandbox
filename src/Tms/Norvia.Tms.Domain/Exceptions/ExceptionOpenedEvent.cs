using Ardalis.SharedKernel;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>The TMS opened an Exception (P7), for the Tracking event that caused it.</summary>
public sealed class ExceptionOpenedEvent(ExceptionCase exception, TrackingEvent cause) : DomainEventBase
{
    public ExceptionCase Exception { get; } = exception;

    public TrackingEvent Cause { get; } = cause;
}
