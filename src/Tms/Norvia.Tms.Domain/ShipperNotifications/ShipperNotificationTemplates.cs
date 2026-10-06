using Norvia.Tms.Domain.Exceptions;
using static System.FormattableString;

namespace Norvia.Tms.Domain.ShipperNotifications;

/// <summary>The message a Shipper notification starts with for each type of Exception (P7), which the Planner can edit before sending.</summary>
public static class ShipperNotificationTemplates
{
    public static string For(ExceptionType type, string? shipperReference)
    {
        var shipment = string.IsNullOrWhiteSpace(shipperReference) ? "your shipment" : Invariant($"your shipment {shipperReference}");
        return type switch
        {
            ExceptionType.CollectionFailed => Invariant($"The collection of {shipment} didn't take place. We are arranging a new collection and will keep you informed."),
            ExceptionType.DeliveryFailed => Invariant($"The delivery of {shipment} didn't succeed. We are arranging a new delivery and will keep you informed."),
            ExceptionType.Damaged => Invariant($"Damage was reported on {shipment}. We are looking into it and will keep you informed."),
            ExceptionType.Reservations => Invariant($"{shipment} was delivered, with reservations noted by the consignee. We are looking into it and will keep you informed."),
            ExceptionType.LateDelivery => Invariant($"The delivery of {shipment} will be later than planned. We will keep you informed of the new expected delivery."),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The TMS has no template for this type of Exception."),
        };
    }
}
