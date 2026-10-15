namespace Norvia.Simulator.Domain.Carriers;

/// <summary>A tracking event a Carrier reports (P5), by the shared status code list.</summary>
public enum CarrierEvent
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
