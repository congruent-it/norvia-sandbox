using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Exceptions.Timeline;

/// <summary>One cause of an Exception on a Shipment's timeline (P7).</summary>
/// <param name="SourceEventId">The sender's own ID of the Tracking event or Proof of delivery; null for a window that passed.</param>
public sealed record ExceptionCauseView(ExceptionCauseKind Kind, string? SourceEventId, DateTimeOffset AddedAt)
{
    public static ExceptionCauseView Of(ExceptionCause cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        return new(cause.Kind, cause.SourceEventId, cause.AddedAt);
    }
}
