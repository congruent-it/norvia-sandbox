using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>P7's Late delivery rules, with the grace from the TMS settings.</summary>
public sealed class LateDeliveryRules(TimeSpan grace)
{
    /// <summary>Whether an ETA is after the delivery window's latest time. A Shipment without an ETA or a window is never late this way.</summary>
    public static bool IsEtaLate(DateTimeOffset? eta, TimeWindow? deliveryWindow) =>
        eta is { } expected && deliveryWindow?.Latest is { } latest && expected > latest;

    /// <summary>Whether the delivery window's latest time, plus the grace, has passed. The caller checks that the Shipment isn't Delivered.</summary>
    public bool HasWindowPassed(TimeWindow? deliveryWindow, DateTimeOffset now) =>
        deliveryWindow?.Latest is { } latest && now >= latest + grace;
}
