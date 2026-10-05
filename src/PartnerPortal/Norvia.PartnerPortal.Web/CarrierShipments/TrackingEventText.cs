using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierShipments;

/// <summary>Words for a Shipment's stage and for the Tracking events a Carrier sends, as the Carrier's own status codes name them.</summary>
public static class TrackingEventText
{
    public static string Stage(ShipmentStage stage) => stage switch
    {
        ShipmentStage.Booked => "Booked",
        ShipmentStage.Collected => "Collected",
        ShipmentStage.InTransit => "In transit",
        ShipmentStage.Delivered => "Delivered",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Every stage has words."),
    };

    /// <summary>The event as its button reads, with the Carrier's status code the message will carry.</summary>
    public static string Button(TrackingEventType type) => type switch
    {
        TrackingEventType.Collected => "Collected (PU)",
        TrackingEventType.Departed => "Departed (DEP)",
        TrackingEventType.Arrived => "Arrived (ARR)",
        TrackingEventType.OutForDelivery => "Out for delivery (OFD)",
        TrackingEventType.Delivered => "Delivered (POD)",
        TrackingEventType.CollectionFailed => "Collection failed (NPU)",
        TrackingEventType.DeliveryFailed => "Delivery failed (NDL)",
        TrackingEventType.Damaged => "Damaged (DMG)",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Every Tracking event has words."),
    };
}
