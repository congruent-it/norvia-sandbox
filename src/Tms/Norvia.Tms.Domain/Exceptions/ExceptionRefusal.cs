using static System.FormattableString;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>Why the TMS refused a Planner's action on an Exception: a stable code and a readable message.</summary>
public sealed record ExceptionRefusal(ExceptionRefusalCode Code, string Message)
{
    public static ExceptionRefusal NotFound(string exceptionNumber) =>
        new(ExceptionRefusalCode.ExceptionNotFound, Invariant($"The TMS has no Exception {exceptionNumber}."));

    public static ExceptionRefusal ShipperNotificationNotFound(string notificationId) =>
        new(ExceptionRefusalCode.ShipperNotificationNotFound, Invariant($"The TMS has no Shipper notification {notificationId}."));
}
