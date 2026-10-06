using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

/// <summary>A Shipper notification the Planner requested from an Exception, made the way the TMS makes it.</summary>
internal static class ShipperNotificationSamples
{
    public static ShipperNotification Requested(ExceptionCase exception, DateTimeOffset at) =>
        ShipperNotification.Request(exception, "SHIP-REF-1", "Your shipment is delayed.", "Anna de Vries", null, null, new CorrelationId("corr-notify"), at);
}
