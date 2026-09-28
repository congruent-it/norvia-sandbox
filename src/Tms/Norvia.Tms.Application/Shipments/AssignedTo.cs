using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments;

/// <summary>Whether a Shipment is carried by Norvia's Own fleet or by a Carrier.</summary>
public enum AssignedTo
{
    OwnFleet,
    Carrier,
}

public static class AssignedToExtensions
{
    /// <summary>Whether Own fleet or a Carrier carries the Shipment, as events and the query side name it.</summary>
    public static AssignedTo AssignedTo(this CarriedBy carriedBy)
    {
        ArgumentNullException.ThrowIfNull(carriedBy);
        return carriedBy.IsOwnFleet ? Shipments.AssignedTo.OwnFleet : Shipments.AssignedTo.Carrier;
    }
}
