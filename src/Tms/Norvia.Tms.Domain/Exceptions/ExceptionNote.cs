using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>
/// A free-text note a Planner added to an open Exception (P7), so a phone call or an agreement made outside the TMS is on
/// record. It starts in the TMS UI and so has a correlation ID of its own.
/// </summary>
/// <param name="Planner">Who wrote it, as the TMS UI names them.</param>
public sealed record ExceptionNote(string Text, string Planner, CorrelationId CorrelationId, DateTimeOffset AddedAt);
