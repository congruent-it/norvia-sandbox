using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.UnitTests.ProofsOfDelivery;

internal static class ProofsOfDeliverySamples
{
    public static readonly CarrierId VanDijk = new("CAR-0001");
    public static readonly CarrierBookingNumber BookingNumber = CarrierBookingNumber.FromSequence(1);
    public static readonly DateTimeOffset DeliveryTime = new(2026, 9, 25, 14, 30, 0, TimeSpan.FromHours(2));

    /// <summary>A Carrier Shipment Van Dijk confirmed, and carried on as far as the status says.</summary>
    public static Shipment ShipmentCarriedTo(ShipmentStatus status)
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(VanDijk), sequence: 12);
        if (status >= ShipmentStatus.Released)
        {
            shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(1), BookingNumber);
        }

        if (status >= ShipmentStatus.Booked)
        {
            shipment.Book();
        }

        if (status >= ShipmentStatus.Collected)
        {
            var type = status == ShipmentStatus.Delivered ? TrackingEventType.Delivered : TrackingEventType.Collected;
            shipment.Record(
                new TrackingReport(
                    TrackingSource.Carrier(VanDijk), "evt-progress", "VDT-1", null, type, Samples.Now.AddHours(3),
                    new TrackingLocation("Düsseldorf", "40213", "DE"), null, null),
                new CorrelationId("corr-progress"),
                Samples.Now.AddHours(3));
        }

        shipment.ClearDomainEvents();
        return shipment;
    }

    public static CarrierBooking ConfirmedBooking(Shipment shipment)
    {
        var booking = CarrierBooking.Request(BookingNumber, shipment.Id, shipment.Number, VanDijk, new CorrelationId("corr-release"), Samples.Now.AddHours(1));
        booking.Confirm("VDT-1", plannedCollectionTime: null, shipment.TransportOrderNumber, new CorrelationId("corr-answer"), Samples.Now.AddHours(2));
        return booking;
    }

    public static ProofOfDeliveryFile Cmr() => new(Guid.Parse("0197a1b2-0000-7000-8000-000000000001"), "cmr.pdf", "application/pdf", 4);

    public static ProofOfDeliveryReport Report(ProofOfDeliverySource? source = null, string reference = "VDT-1", IReadOnlyList<ProofOfDeliveryFile>? files = null) =>
        new(
            source ?? ProofOfDeliverySource.Carrier(VanDijk),
            "doc-1",
            reference,
            "J. Becker",
            DeliveryTime,
            new TrackingLocation("Düsseldorf", "40213", "DE"),
            DeliveryCondition.Clean,
            "Signed at the dock",
            files ?? [Cmr()]);
}
