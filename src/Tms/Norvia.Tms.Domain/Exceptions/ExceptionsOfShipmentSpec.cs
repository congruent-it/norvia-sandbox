using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>Every Exception of a Shipment, open or resolved, in the order they opened, for the Shipment's timeline (P7).</summary>
public sealed class ExceptionsOfShipmentSpec : Specification<ExceptionCase>
{
    public ExceptionsOfShipmentSpec(ShipmentId shipmentId)
    {
        ShipmentId = shipmentId;
        Query.Where(exception => exception.ShipmentId == shipmentId)
            .OrderBy(exception => exception.OpenedAt)
            .ThenBy(exception => exception.Number)
            .AsNoTracking();
    }

    public ShipmentId ShipmentId { get; }
}
