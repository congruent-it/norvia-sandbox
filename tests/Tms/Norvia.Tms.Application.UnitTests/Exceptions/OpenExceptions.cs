using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

/// <summary>An Exception a Delivery failed Tracking event opened, made the way the TMS makes it.</summary>
internal static class OpenExceptions
{
    public static ExceptionCase DeliveryFailed(
        CarriedBy? carriedBy = null, long orderSequence = 7, long shipmentSequence = 12, long number = 5, DateTimeOffset? at = null)
    {
        var order = Samples.AcceptedOrder(sequence: orderSequence);
        var shipment = Samples.Planned(order, carriedBy ?? CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: shipmentSequence);
        if (shipment.CarriedBy.IsOwnFleet)
        {
            shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(3));
        }
        else
        {
            shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(3), CarrierBookingNumber.FromSequence(42));
        }

        shipment.Book();
        var cause = shipment.Record(
            new TrackingReport(
                shipment.CarriedBy.CarrierId is { } carrierId ? TrackingSource.Carrier(carrierId) : TrackingSource.OwnFleet,
                "evt-ndl",
                "VDT-1",
                null,
                TrackingEventType.DeliveryFailed,
                Samples.Now.AddDays(1),
                new TrackingLocation("Rotterdam", "3011 AA", "NL"),
                null,
                "Consignee closed"),
            new CorrelationId("corr-cause"),
            at ?? Samples.Now.AddDays(1));
        return ExceptionCase.Open(ExceptionNumber.FromSequence(number), shipment, cause, at ?? Samples.Now.AddDays(1));
    }
}
