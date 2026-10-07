namespace Norvia.Tms.Domain.ShipperTracking;

/// <summary>Why the TMS refused a Shipper's tracking query: a stable code and a readable message.</summary>
public sealed record ShipperTrackingRefusal(ShipperTrackingRefusalCode Code, string Message)
{
    public static ShipperTrackingRefusal TransportOrderNotFound() =>
        new(ShipperTrackingRefusalCode.TransportOrderNotFound, "The Transport Order is not known to the TMS.");

    public static ShipperTrackingRefusal ProofOfDeliveryNotFound() =>
        new(ShipperTrackingRefusalCode.ProofOfDeliveryNotFound, "The Transport Order has no current Proof of delivery with such a file.");
}
