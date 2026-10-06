using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>One cause of an Exception (P7): the Tracking event that opened it, or a later one of the same type that joined it.</summary>
/// <param name="SourceEventId">The Carrier's own ID for the Tracking event.</param>
public sealed record ExceptionCause(TrackingEventId TrackingEventId, string SourceEventId, CorrelationId CorrelationId, DateTimeOffset AddedAt);
