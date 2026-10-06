namespace Norvia.Tms.Domain.Exceptions;

/// <summary>
/// What went wrong with a Shipment (P7). A Shipment has at most one open Exception of each type. The names are stable:
/// never rename one, only add.
/// </summary>
public enum ExceptionType
{
    /// <summary>A Delivery failed Tracking event (Carrier code <c>NDL</c>).</summary>
    DeliveryFailed = 1,
}
