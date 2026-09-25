namespace Norvia.PartnerPortal.Domain.Bookings;

/// <summary>
/// Norvia's answer to a booking, as the party outside Norvia got it: a Transport Order number when Norvia booked it,
/// every reason when Norvia rejected it, or that it wasn't received or was refused.
/// </summary>
public sealed record NorviaAnswer
{
    private NorviaAnswer(BookingResult result, string? messageId)
    {
        Result = result;
        MessageId = messageId;
    }

    public BookingResult Result { get; }

    /// <summary>The correlation ID Norvia gave the attempt, by which Norvia finds every trace of it. Null when Norvia never answered.</summary>
    public string? MessageId { get; }

    /// <summary>The Transport Order the booking is under; null unless Norvia booked it.</summary>
    public string? TransportOrderNumber { get; private init; }

    /// <summary>Every rule a Rejected booking broke; empty for any other answer.</summary>
    public IReadOnlyList<RejectionReason> Reasons { get; private init; } = [];

    /// <summary>Norvia's own words for a refusal; null for any other answer.</summary>
    public string? Text { get; private init; }

    /// <summary>Whether a Transport Order exists for the booking, new or from an earlier attempt.</summary>
    public bool IsBooked => TransportOrderNumber is not null;

    public static NorviaAnswer Accepted(string transportOrderNumber, string? messageId) =>
        Booked(BookingResult.Accepted, transportOrderNumber, messageId);

    public static NorviaAnswer AlreadyBooked(string transportOrderNumber, string? messageId) =>
        Booked(BookingResult.AlreadyBooked, transportOrderNumber, messageId);

    public static NorviaAnswer Rejected(IReadOnlyList<RejectionReason> reasons, string? messageId)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        if (reasons.Count == 0)
        {
            throw new ArgumentException("Norvia rejects a booking for at least one reason.", nameof(reasons));
        }

        return new NorviaAnswer(BookingResult.Rejected, messageId) { Reasons = [.. reasons] };
    }

    public static NorviaAnswer NotReceived(string? messageId) => new(BookingResult.NotReceived, messageId);

    public static NorviaAnswer Refused(string text, string? messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new NorviaAnswer(BookingResult.Refused, messageId) { Text = text };
    }

    private static NorviaAnswer Booked(BookingResult result, string transportOrderNumber, string? messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transportOrderNumber);
        return new NorviaAnswer(result, messageId) { TransportOrderNumber = transportOrderNumber };
    }
}
