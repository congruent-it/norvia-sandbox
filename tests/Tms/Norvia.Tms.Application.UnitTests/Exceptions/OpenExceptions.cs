using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
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
        return ExceptionCase.Open(ExceptionNumber.FromSequence(number), shipment, ExceptionCause.From(cause, at ?? Samples.Now.AddDays(1))!);
    }

    /// <summary>A Late delivery Exception the time-based check opened.</summary>
    public static ExceptionCase LateDelivery()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12);
        var exception = ExceptionCase.Open(
            ExceptionNumber.FromSequence(5), shipment, ExceptionCause.WindowPassed(shipment, new CorrelationId("corr-check"), Samples.Now.AddDays(2)));
        exception.ClearDomainEvents();
        return exception;
    }

    /// <summary>A Reservations Exception a Proof of delivery with reservations opened.</summary>
    public static ExceptionCase Reservations()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12);
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(3), CarrierBookingNumber.FromSequence(42));
        shipment.Book();
        var proof = ProofOfDelivery.Record(
            shipment,
            new ProofOfDeliveryReport(
                ProofOfDeliverySource.Carrier(new CarrierId("CAR-0001")),
                "doc-1",
                "VDT-1",
                "J. Becker",
                Samples.Now.AddDays(1),
                new TrackingLocation("Rotterdam", "3011 AA", "NL"),
                DeliveryCondition.Reservations,
                "Two pallets short",
                [new ProofOfDeliveryFile(Guid.NewGuid(), "cmr.pdf", "application/pdf", 4)]),
            new CorrelationId("corr-pod"),
            Samples.Now.AddDays(1));
        return ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, ExceptionCause.From(proof, Samples.Now.AddDays(1))!);
    }
}
