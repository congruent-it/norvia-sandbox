using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.Exceptions.Timeline;

/// <summary>
/// An Exception on a Shipment's timeline (P7), open or resolved: its causes, what the Planner did about it, its Shipper
/// notifications and, once resolved, its outcome and remark.
/// </summary>
/// <param name="Notifications">The Shipper notifications the Planner sent from it, in the order they were requested.</param>
/// <param name="CarrierBookingCancellations">The Carrier booking cancellations the Planner made from it.</param>
/// <param name="Outcome">The Planner's outcome once resolved; null while open.</param>
public sealed record ExceptionTimelineEntry(
    string ExceptionNumber,
    ExceptionType Type,
    ExceptionStatus Status,
    DateTimeOffset OpenedAt,
    bool AfterDelivered,
    bool CauseCleared,
    IReadOnlyList<ExceptionCauseView> Causes,
    IReadOnlyList<ExceptionNoteView> Notes,
    IReadOnlyList<ShipperNotificationView> Notifications,
    IReadOnlyList<CarrierBookingCancellationView> CarrierBookingCancellations,
    ExceptionOutcome? Outcome,
    string? Remark,
    DateTimeOffset? ResolvedAt)
{
    public static ExceptionTimelineEntry Of(
        ExceptionCase exception, IEnumerable<ShipperNotification> notifications, IEnumerable<CarrierBooking> cancelledBookings)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(cancelledBookings);
        return new(
            exception.Number.Value,
            exception.Type,
            exception.Status,
            exception.OpenedAt,
            exception.AfterDelivered,
            exception.CauseCleared,
            [.. exception.Causes.Select(ExceptionCauseView.Of)],
            [.. exception.Notes.Select(ExceptionNoteView.Of)],
            [.. notifications.Where(notification => notification.ExceptionId == exception.Id).OrderBy(notification => notification.RequestedAt).Select(ShipperNotificationView.Of)],
            [.. cancelledBookings.Where(booking => booking.CancellationExceptionNumber == exception.Number).OrderBy(booking => booking.CancelledAt).Select(CarrierBookingCancellationView.Of)],
            exception.Outcome,
            exception.Remark,
            exception.ResolvedAt);
    }
}
