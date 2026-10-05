namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>What a Carrier reports happened to a Shipment (P5). The first five move it along, the last three are exceptions.</summary>
public enum TrackingEventType
{
    Collected,
    Departed,
    Arrived,
    OutForDelivery,
    Delivered,
    CollectionFailed,
    DeliveryFailed,
    Damaged,
}
