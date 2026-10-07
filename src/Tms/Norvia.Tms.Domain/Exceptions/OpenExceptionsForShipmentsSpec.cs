using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>The IDs of those of several Shipments that have an open Exception, for enriching a list built from them (P7).</summary>
public sealed class OpenExceptionsForShipmentsSpec : Specification<ExceptionCase, ShipmentId>
{
    public OpenExceptionsForShipmentsSpec(IReadOnlyCollection<ShipmentId> shipmentIds)
    {
        ArgumentNullException.ThrowIfNull(shipmentIds);
        Query.Where(exception => shipmentIds.Contains(exception.ShipmentId) && exception.Status == ExceptionStatus.Open)
            .AsNoTracking()
            .Select(exception => exception.ShipmentId);
    }
}
