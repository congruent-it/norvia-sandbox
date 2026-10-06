using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>The open Exception of the type for the Shipment, tracked, so a new cause that joins it is saved. A Shipment has at most one.</summary>
public sealed class OpenExceptionOfTypeForShipmentSpec : SingleResultSpecification<ExceptionCase>
{
    public OpenExceptionOfTypeForShipmentSpec(ShipmentId shipmentId, ExceptionType type)
    {
        ShipmentId = shipmentId;
        Type = type;
        Query.Where(exception => exception.ShipmentId == shipmentId && exception.Type == type && exception.Status == ExceptionStatus.Open);
    }

    public ShipmentId ShipmentId { get; }

    public ExceptionType Type { get; }
}
