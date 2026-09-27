using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments;

/// <summary>Hands out Shipment numbers, each only once.</summary>
public interface IShipmentNumbers
{
    Task<ShipmentNumber> NextAsync(CancellationToken cancellationToken);
}
