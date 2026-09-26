namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// Why Rating failed for a Transport Order. The names are stable codes that monitoring relies on: never rename one,
/// only add.
/// </summary>
public enum RatingFailureCode
{
    /// <summary>The Shipper has no Rate Card.</summary>
    NoRateCard = 1,

    /// <summary>No version of the Rate Card is valid on the collection date.</summary>
    NoValidRateCardVersion,

    /// <summary>No Lane covers the origin and destination Zones.</summary>
    NoMatchingLane,

    /// <summary>The total weight is above the Lane's highest Weight Break.</summary>
    WeightAboveHighestBreak,
}
