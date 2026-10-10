using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>Finds the Shipments on a Trip, or about to go on one, together with the Transport Order details their Stops come from.</summary>
public sealed class TripShipments(IReadRepository<Shipment> shipments, IReadRepository<TransportOrder> orders)
{
    /// <summary>The Shipments with these IDs, in the order of the IDs; one that doesn't exist is left out.</summary>
    public async Task<IReadOnlyList<ShipmentOnTrip>> OfAsync(IReadOnlyList<ShipmentId> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        var found = await shipments.ListAsync(new ShipmentsByIdsSpec(ids), cancellationToken);
        return await WithDetailsAsync([.. found.OrderBy(shipment => IndexIn(ids, shipment.Id))], cancellationToken);
    }

    /// <summary>The Shipments with these numbers, in the order of the numbers; one that doesn't exist is left out.</summary>
    public async Task<IReadOnlyList<ShipmentOnTrip>> ByNumbersAsync(IReadOnlyList<ShipmentNumber> numbers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        if (numbers.Count == 0)
        {
            return [];
        }

        var found = await shipments.ListAsync(new ShipmentsByNumbersSpec(numbers), cancellationToken);
        return await WithDetailsAsync([.. found.OrderBy(shipment => IndexIn(numbers, shipment.Number))], cancellationToken);
    }

    /// <summary>The Shipments with these numbers as the Planner typed or picked them, each once, and a refusal for each that is no Shipment.</summary>
    public async Task<(IReadOnlyList<ShipmentOnTrip> Found, IReadOnlyList<TripRefusal> NotFound)> FindAsync(IReadOnlyList<string> numbers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        var distinct = numbers.Distinct(StringComparer.Ordinal).ToList();
        var parsed = distinct.Where(number => ShipmentNumber.TryParse(number, out _)).Select(ShipmentNumber.Parse).ToList();
        var found = await ByNumbersAsync(parsed, cancellationToken);
        var known = found.Select(item => item.Shipment.Number.Value).ToHashSet(StringComparer.Ordinal);
        return (found, [.. distinct.Where(number => !known.Contains(number)).Select(TripRefusal.ShipmentNotFound)]);
    }

    private static int IndexIn<T>(IReadOnlyList<T> items, T item)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(items[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private async Task<IReadOnlyList<ShipmentOnTrip>> WithDetailsAsync(IReadOnlyList<Shipment> found, CancellationToken cancellationToken)
    {
        if (found.Count == 0)
        {
            return [];
        }

        var carriedOut = await orders.ListAsync(new TransportOrdersByIdsSpec([.. found.Select(shipment => shipment.TransportOrderId).Distinct()]), cancellationToken);
        var detailsById = carriedOut.ToDictionary(order => order.Id, order => order.Details);
        return
        [
            .. found
                .Where(shipment => detailsById.ContainsKey(shipment.TransportOrderId))
                .Select(shipment => new ShipmentOnTrip(shipment, detailsById[shipment.TransportOrderId])),
        ];
    }
}
