using Norvia.Tms.Application.Exceptions.Timeline;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Web.Shipments;

namespace Norvia.Tms.Web.Exceptions;

/// <summary>Tells an Exception on a Shipment's timeline in the order it happened: its causes, what the Planner did about it and how it was resolved (P7).</summary>
public static class ExceptionStory
{
    public static IReadOnlyList<ExceptionHappening> Of(ExceptionTimelineEntry exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var happenings = new List<ExceptionHappening>();
        happenings.AddRange(exception.Causes.Select(cause => new ExceptionHappening(
            cause.AddedAt, $"Cause: {ExceptionText.Cause(cause.Kind)}", cause.SourceEventId is { } id ? $"Reported as {id}" : string.Empty)));
        happenings.AddRange(exception.Notes.Select(note => new ExceptionHappening(note.AddedAt, $"Note by {note.Planner}", note.Text)));
        happenings.AddRange(exception.Notifications.Select(notification => new ExceptionHappening(
            notification.RequestedAt,
            $"Shipper notification {ExceptionText.Notification(notification.Status).ToLowerInvariant()} (by {notification.Planner})",
            notification.Status == ShipperNotificationStatus.NotDelivered && notification.NotDeliveredMessage is { } cause
                ? $"{notification.Message} Not delivered: {cause}"
                : notification.Message)));
        happenings.AddRange(exception.CarrierBookingCancellations.Select(cancellation => new ExceptionHappening(
            cancellation.CancelledAt,
            $"Carrier booking {cancellation.CarrierBookingNumber} cancelled",
            cancellation.CarrierNotTold ? $"{cancellation.Reason} {CarrierBookingTextNotTold(cancellation)}" : cancellation.Reason)));
        if (exception is { ResolvedAt: { } resolvedAt, Outcome: { } outcome })
        {
            happenings.Add(new ExceptionHappening(resolvedAt, $"Resolved: {ExceptionText.Outcome(outcome)}", exception.Remark ?? string.Empty));
        }

        // A stable sort, so what happened at the same moment stays in the order it is told above.
        return [.. happenings.OrderBy(happening => happening.When)];
    }

    private static string CarrierBookingTextNotTold(CarrierBookingCancellationView cancellation) =>
        $"{CarrierBookingText.CarrierNotTold}: {cancellation.CarrierNotToldMessage}";
}
