using System.Globalization;
using Ardalis.Result;
using Mediator;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.Shipments.Release;
using Norvia.Tms.Application.TransportOrders.Assign;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>What the demo's days share: how a day is found from the start and how a seeded Transport Order is booked for it.</summary>
internal static class DemoBookings
{
    /// <summary>The day itself, or the next weekday when it falls on a weekend, so Drivers are rostered to work.</summary>
    public static DateOnly WeekdayOnOrAfter(DateOnly day)
    {
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    /// <summary>
    /// Books each seeded Transport Order for <paramref name="day"/> and assigns it to Own fleet, through the commands of the Planner and the Partner API, and
    /// returns the Shipment numbers. Repeating it changes nothing: the booking is the same Shipper reference and the assignment the same key under <paramref name="keyPrefix"/>.
    /// </summary>
    public static async Task<IReadOnlyList<string>> BookAndAssignAsync(
        IMediator mediator, string shipperId, DemoCollection collection, DateOnly day, IEnumerable<DemoTransportOrder> orders, TimeZoneInfo zone, string keyPrefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(orders);
        var shipments = new List<string>();
        foreach (var order in orders)
        {
            var submitted = Succeeded(await mediator.Send(
                new SubmitTransportOrderCommand(For(shipperId, collection, day, order, zone), IntakeChannel.ShipperApi, CorrelationId.New().Value), cancellationToken), order.ShipperReference);
            if (submitted.TransportOrderNumber is not { } number)
            {
                throw new InvalidOperationException($"The TMS rejected the demo booking {order.ShipperReference}.");
            }

            shipments.Add(Succeeded(await mediator.Send(
                new AssignTransportOrderCommand(number, CarrierId: null, $"{keyPrefix}-{order.ShipperReference}-{day:yyyyMMdd}", CorrelationId.New().Value), cancellationToken), number).ShipmentNumber);
        }

        return shipments;
    }

    /// <summary>Releases the Shipments, last, so none is on the list of Shipments to put on Trips before all of them are planned.</summary>
    public static async Task ReleaseAsync(IMediator mediator, IEnumerable<string> shipments, DateOnly day, string keyPrefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(shipments);
        foreach (var shipment in shipments)
        {
            Succeeded(await mediator.Send(new ReleaseShipmentCommand(shipment, $"{keyPrefix}-release-{shipment}-{day:yyyyMMdd}", CorrelationId.New().Value), cancellationToken), shipment);
        }
    }

    /// <summary>The value of a successful result; a refusal throws, so the seeder is tried again.</summary>
    public static T Succeeded<T>(Result<T> result, string what) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Seeding the demo failed at {what}: {result.Status} {string.Join("; ", result.ValidationErrors.Select(error => error.ErrorMessage))}.");

    /// <summary>The booking for one seeded Transport Order, collected on <paramref name="day"/> in <paramref name="zone"/>. Its Shipper reference carries the day, so a demo started on another day books its own.</summary>
    public static CanonicalTransportOrder For(string shipperId, DemoCollection collection, DateOnly day, DemoTransportOrder order, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(zone);
        var delivery = order.Delivery;
        return new CanonicalTransportOrder(
            null,
            shipperId,
            string.Create(CultureInfo.InvariantCulture, $"{order.ShipperReference}-{day:yyyyMMdd}"),
            new CanonicalCollectionDetails(
                new CanonicalAddress(collection.Street, collection.Postcode, collection.City, collection.Country),
                new CanonicalTimeWindow(At(day, collection.Earliest, zone), At(day, collection.Latest, zone))),
            new CanonicalDeliveryDetails(
                new CanonicalAddress(delivery.Street, delivery.Postcode, delivery.City, delivery.Country),
                new CanonicalConsignee(delivery.Consignee.Name, delivery.Consignee.ContactName, delivery.Consignee.Phone, null),
                null),
            [new CanonicalGoodsLine("pallet", order.Pallets, order.Pallets * order.WeightKgPerPallet, null)],
            null);
    }

    private static DateTimeOffset At(DateOnly day, string time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture));
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
