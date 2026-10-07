using Norvia.Tms.Application.Exceptions.ListOpen;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Web.Exceptions;

/// <summary>Words an Exception as a Planner reads it, in the glossary's terms.</summary>
public static class ExceptionText
{
    /// <summary>The marker on an Exception whose cause no longer holds (P7).</summary>
    public const string CauseCleared = "Cause cleared";

    /// <summary>The marker on an Exception a Tracking event opened after the Shipment was Delivered (P7).</summary>
    public const string AfterDelivered = "After Delivered";

    /// <summary>The marker on a Shipment in progress that has an open Exception (P7).</summary>
    public const string OpenException = "Open Exception";

    public const string NotNotified = "Not notified";

    /// <summary>What went wrong, such as "Delivery failed".</summary>
    public static string Type(ExceptionType type) => type switch
    {
        ExceptionType.DeliveryFailed => "Delivery failed",
        ExceptionType.CollectionFailed => "Collection failed",
        ExceptionType.Damaged => "Damaged",
        ExceptionType.Reservations => "Reservations",
        ExceptionType.LateDelivery => "Late delivery",
        _ => type.ToString(),
    };

    /// <summary>How the Planner resolved an Exception, such as "Replanned".</summary>
    public static string Outcome(ExceptionOutcome outcome) => outcome switch
    {
        ExceptionOutcome.NoActionNeeded => "No action needed",
        ExceptionOutcome.Redelivered => "Redelivered",
        ExceptionOutcome.Replanned => "Replanned",
        ExceptionOutcome.ReturnedToShipper => "Returned to Shipper",
        ExceptionOutcome.ClaimHandledOutsideTms => "Claim handled outside the TMS",
        _ => outcome.ToString(),
    };

    /// <summary>Where a Shipper notification stands, such as "Not delivered".</summary>
    public static string Notification(ShipperNotificationStatus status) => status switch
    {
        ShipperNotificationStatus.NotDelivered => "Not delivered",
        _ => status.ToString(),
    };

    /// <summary>The last Shipper notification's status of an Exception, or that the Shipper wasn't notified.</summary>
    public static string LastNotification(OpenExceptionSummary exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.LastShipperNotificationStatus is { } status ? Notification(status) : NotNotified;
    }

    /// <summary>What caused an Exception, such as "Tracking event".</summary>
    public static string Cause(ExceptionCauseKind kind) => kind switch
    {
        ExceptionCauseKind.TrackingEvent => "Tracking event",
        ExceptionCauseKind.ProofOfDelivery => "Proof of delivery",
        ExceptionCauseKind.WindowPassed => "Delivery window passed",
        _ => kind.ToString(),
    };
}
