namespace Norvia.PartnerPortal.Application.ShipperTracking;

/// <summary>Where a Transport Order stands, as the Shipper sees it (P8). Scheduled covers everything before collection.</summary>
public enum ShipperTrackingStatus
{
    Received,
    Scheduled,
    Collected,
    InTransit,
    Delivered,
}
