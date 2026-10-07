using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>
/// The open Exceptions of a Shipment, whatever their type, except the one a transaction is resolving (P9): the store still has
/// that one as open until the transaction saves.
/// </summary>
public sealed class OpenExceptionOfShipmentSpec : Specification<ExceptionCase>
{
    public OpenExceptionOfShipmentSpec(ShipmentId shipmentId, ExceptionId? except = null)
    {
        Query.Where(exception => exception.ShipmentId == shipmentId && exception.Status == ExceptionStatus.Open)
            .AsNoTracking();
        if (except is { } resolving)
        {
            Query.Where(exception => exception.Id != resolving);
        }
    }
}
