using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>
/// P7's Late delivery rules, with the grace from the TMS settings. A grace that is not set (null) switches the time-based check off: the
/// window passing never counts. A Late delivery from an ETA (<see cref="IsEtaLate"/>) does not depend on it.
/// </summary>
public sealed class LateDeliveryRules(TimeSpan? grace)
{
    /// <summary>The rules with the time-based check switched off, as the as-found estate has them.</summary>
    public static LateDeliveryRules SwitchedOff { get; } = new(null);

    /// <summary>Whether an ETA is after the delivery window's latest time. A Shipment without an ETA or a window is never late this way.</summary>
    public static bool IsEtaLate(DateTimeOffset? eta, TimeWindow? deliveryWindow) =>
        eta is { } expected && deliveryWindow?.Latest is { } latest && expected > latest;

    /// <summary>Whether the delivery window's latest time, plus the grace, has passed. The caller checks that the Shipment isn't Delivered.</summary>
    public bool HasWindowPassed(TimeWindow? deliveryWindow, DateTimeOffset now) =>
        grace is { } allowed && deliveryWindow?.Latest is { } latest && now >= latest + allowed;
}
