using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments;

/// <summary>Who carries a Shipment: Own fleet, or the Carrier it names.</summary>
/// <param name="Carrier">Null for Own fleet.</param>
public sealed record CarriedByView(AssignedTo AssignedTo, CarrierView? Carrier)
{
    public static CarriedByView Of(CarriedBy carriedBy, ICarrierDirectory carriers)
    {
        ArgumentNullException.ThrowIfNull(carriedBy);
        return carriedBy.CarrierId is { } carrierId
            ? new(AssignedTo.Carrier, CarrierView.Of(carrierId, carriers))
            : new(AssignedTo.OwnFleet, null);
    }
}
