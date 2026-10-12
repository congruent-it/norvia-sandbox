using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Trips;

/// <summary>A Shipment Awaiting re-delivery (P12) goes on a later Trip with only its delivery Stop; Dispatch clears the flag and taking it off sets it again.</summary>
public sealed class TripRedeliveryTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly Vehicle Truck = new(new VehicleId("VEH-0002"), "TT-201-R", VehicleType.RigidTruck, 8000, 18, true);

    private static readonly Driver Joost = new(new DriverId("DRV-0001"), "Joost Bakker", new HashSet<LicenceCategory> { LicenceCategory.C }, true);

    [Fact]
    public void AddShipment_ARedelivery_PutsOnlyItsDeliveryStopOnTheTrip()
    {
        var trip = Draft();
        var other = ShipmentId.New();
        var redelivery = ShipmentId.New();

        trip.AddShipment(other).Should().BeTrue();
        trip.AddShipment(redelivery, redelivery: true).Should().BeTrue();

        trip.Stops.Select(stop => (stop.ShipmentId, stop.Kind, stop.Position)).Should().Equal(
            (other, StopKind.Collection, 0), (other, StopKind.Delivery, 1), (redelivery, StopKind.Delivery, 2));
        trip.IsRedelivery(redelivery).Should().BeTrue();
        trip.IsRedelivery(other).Should().BeFalse();
    }

    [Fact]
    public void ReorderStops_WithARedelivery_KeepsItsDeliveryStopOnly()
    {
        var trip = Draft();
        var other = ShipmentId.New();
        var redelivery = ShipmentId.New();
        trip.AddShipment(other);
        trip.AddShipment(redelivery, redelivery: true);

        var fault = trip.ReorderStops([(redelivery, StopKind.Delivery), (other, StopKind.Collection), (other, StopKind.Delivery)]);

        fault.Should().BeNull();
        trip.Stops[0].ShipmentId.Should().Be(redelivery);
    }

    [Fact]
    public void Dispatch_ATripCarryingARedelivery_ClearsTheFlag_AndTheShipmentStaysCollected()
    {
        var shipment = AwaitingRedelivery(1);
        var trip = Draft();
        trip.AddShipment(shipment.Id, redelivery: true);

        trip.Dispatch(new CorrelationId("dispatch"), At, Truck, Joost, [new DispatchedShipment(shipment, TransportOrderTests.SampleDetails())]);

        shipment.AwaitingRedelivery.Should().BeFalse();
        shipment.Status.Should().Be(ShipmentStatus.Collected);
        trip.Status.Should().Be(TripStatus.Dispatched);
    }

    [Fact]
    public void Amend_ADispatchedTripCarryingAClearedRedelivery_StillSendsTheNextVersion()
    {
        var shipment = AwaitingRedelivery(1);
        var trip = Draft();
        trip.AddShipment(shipment.Id, redelivery: true);
        var onTrip = new[] { new DispatchedShipment(shipment, TransportOrderTests.SampleDetails()) };
        trip.Dispatch(new CorrelationId("dispatch"), At, Truck, Joost, onTrip);

        trip.Amend(new CorrelationId("amend"), At, Truck, Joost, onTrip);

        trip.Version.Should().Be(2);
        shipment.AwaitingRedelivery.Should().BeFalse();
    }

    [Fact]
    public void TakeShipmentOff_ARedelivery_SetsTheFlagAgain_AndLeavesItCollected()
    {
        var shipment = AwaitingRedelivery(1);
        var trip = Draft();
        trip.AddShipment(shipment.Id, redelivery: true);
        trip.Dispatch(new CorrelationId("dispatch"), At, Truck, Joost, [new DispatchedShipment(shipment, TransportOrderTests.SampleDetails())]);
        trip.EnsureCanTakeOff(shipment).Should().BeNull();

        trip.TakeShipmentOff(shipment, "Consignee closed", new CorrelationId("off"), At, new NextTripVersion(Truck, Joost, []));

        shipment.AwaitingRedelivery.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Collected);
        trip.Stops.Should().BeEmpty();
    }

    [Fact]
    public void TakeShipmentOff_ARedeliveryOnADraft_KeepsTheFlag()
    {
        var shipment = AwaitingRedelivery(1);
        var trip = Draft();
        trip.AddShipment(shipment.Id, redelivery: true);

        trip.TakeShipmentOff(shipment, "Wrong day", new CorrelationId("off"), At);

        shipment.AwaitingRedelivery.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Collected);
        trip.Stops.Should().BeEmpty();
    }

    [Fact]
    public void EnsureCanTakeOff_ADeliveredRedelivery_IsShipmentCollected()
    {
        var shipment = AwaitingRedelivery(1);
        var trip = Draft();
        trip.AddShipment(shipment.Id, redelivery: true);
        shipment.Dispatch();
        shipment.Record(Report(shipment, TrackingEventType.Delivered), new CorrelationId("driver"), At);

        trip.EnsureCanTakeOff(shipment)!.Code.Should().Be(TripRefusalCode.ShipmentCollected);
    }

    [Fact]
    public void Dispatch_ARedeliveryThatWasDispatchedAlready_IsRefusedAgain()
    {
        var shipment = AwaitingRedelivery(1);
        shipment.Dispatch().Should().BeTrue();

        shipment.Dispatch().Should().BeFalse();
    }

    private static Trip Draft()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(2), new DateOnly(2026, 10, 15), Truck.Id, Joost.Id, new IdempotencyKey("k2"));
        trip.Plan(new TimeOnly(7, 30), TimeSpan.FromHours(6));
        return trip;
    }

    private static TrackingReport Report(Shipment shipment, TrackingEventType type) =>
        new(TrackingSource.OwnFleet, Guid.NewGuid().ToString(), null, null, type, At, new TrackingLocation("Utrecht", "3511 AW", "NL"), null, null,
            new OwnFleetReport(TripNumber.FromSequence(1), Joost.Id, shipment.Number, 1));

    // Collected on an earlier Trip, whose Driver couldn't deliver it.
    private static Shipment AwaitingRedelivery(long sequence)
    {
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(sequence), IntakeChannel.ShipperApi, new CorrelationId("i"), At);
        var shipment = order.Plan(ShipmentNumber.FromSequence(sequence), CarriedBy.OwnFleet, new IdempotencyKey($"p{sequence}"), new CorrelationId("p"), At)!;
        shipment.Release(order, new IdempotencyKey($"r{sequence}"), new CorrelationId("r"), At);
        shipment.Dispatch();
        shipment.Record(Report(shipment, TrackingEventType.Collected), new CorrelationId("driver"), At);
        shipment.AwaitRedelivery();
        return shipment;
    }
}
