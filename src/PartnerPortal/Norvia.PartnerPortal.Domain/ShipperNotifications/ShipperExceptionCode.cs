namespace Norvia.PartnerPortal.Domain.ShipperNotifications;

/// <summary>What went wrong, as the Shipper notification format names it (P7): <c>COLLECTION_FAILED</c> and so on.</summary>
public enum ShipperExceptionCode
{
    CollectionFailed,
    DeliveryFailed,
    Damaged,
    Reservations,
    Late,
}

public static class ShipperExceptionCodes
{
    /// <summary>The code the format spells so, or false for any other text.</summary>
    public static bool TryParse(string? wire, out ShipperExceptionCode code)
    {
        (var known, code) = wire switch
        {
            "COLLECTION_FAILED" => (true, ShipperExceptionCode.CollectionFailed),
            "DELIVERY_FAILED" => (true, ShipperExceptionCode.DeliveryFailed),
            "DAMAGED" => (true, ShipperExceptionCode.Damaged),
            "RESERVATIONS" => (true, ShipperExceptionCode.Reservations),
            "LATE" => (true, ShipperExceptionCode.Late),
            _ => (false, default(ShipperExceptionCode)),
        };
        return known;
    }
}
