namespace Norvia.PartnerPortal.Application.ShipperTracking;

/// <summary>The progress a Shipper sees of a Transport Order. Exceptions never reach it (ADR 0011).</summary>
public enum ShipperProgressType
{
    Collected,
    Departed,
    Arrived,
    OutForDelivery,
    Delivered,
}
