using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

/// <summary>
/// The Planner hands a Planned Shipment over by releasing it (P3). Released is as far as a Shipment gets in this build,
/// and P3 can no longer change it.
/// </summary>
public sealed class ShipmentReleaseTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PlannedAt = ReceivedAt.AddHours(2);
    private static readonly DateTimeOffset ReleasedAt = PlannedAt.AddMinutes(30);
    private static readonly CarriedBy VanDijk = CarriedBy.Carrier(new CarrierId("CAR-0001"));
    private static readonly CorrelationId Release = new("corr-release");
    private static readonly IdempotencyKey Key = new("key-rel1");
    private static readonly CarrierBookingNumber BookingNumber = CarrierBookingNumber.FromSequence(42);

    [Fact]
    public void Release_PlannedOwnFleetShipment_MakesItReleased_AndRecordsTheReleaseWithNoCarrierBookingNumber()
    {
        var (order, shipment) = Planned(CarriedBy.OwnFleet);

        var released = shipment.Release(order, Key, Release, ReleasedAt);

        released.Should().Be(new ShipmentRelease(Release, ReleasedAt, Key, null));
        shipment.Status.Should().Be(ShipmentStatus.Released);
        shipment.LastRelease.Should().Be(new ShipmentRelease(Release, ReleasedAt, Key, null));
        shipment.CarriedBy.Should().Be(CarriedBy.OwnFleet);
        shipment.CorrelationId.Should().Be(new CorrelationId("corr-planning"), "the release has its own trail, in its record");
    }

    [Fact]
    public void Release_PlannedCarrierShipment_MakesItReleased_AndRecordsTheReleaseWithTheCarrierBookingNumber()
    {
        var (order, shipment) = Planned(VanDijk);

        var released = shipment.Release(order, Key, Release, ReleasedAt, BookingNumber);

        released.Should().Be(new ShipmentRelease(Release, ReleasedAt, Key, BookingNumber));
        shipment.LastRelease.Should().Be(new ShipmentRelease(Release, ReleasedAt, Key, BookingNumber));
        shipment.CarriedBy.Should().Be(VanDijk);
    }

    [Fact]
    public void Release_CarrierShipmentWithNoCarrierBookingNumber_Throws()
    {
        var (order, shipment) = Planned(VanDijk);

        var release = () => shipment.Release(order, Key, Release, ReleasedAt);

        release.Should().Throw<ArgumentException>();
        shipment.Status.Should().Be(ShipmentStatus.Planned);
    }

    [Fact]
    public void Release_OwnFleetShipmentWithACarrierBookingNumber_Throws()
    {
        var (order, shipment) = Planned(CarriedBy.OwnFleet);

        var release = () => shipment.Release(order, Key, Release, ReleasedAt, BookingNumber);

        release.Should().Throw<ArgumentException>();
        shipment.Status.Should().Be(ShipmentStatus.Planned);
    }

    [Fact]
    public void Release_PlannedShipment_RaisesShipmentReleasedWithItsTransportOrder()
    {
        var (order, shipment) = Planned(CarriedBy.OwnFleet);

        shipment.Release(order, Key, Release, ReleasedAt);

        var released = shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentReleasedEvent>().Subject;
        released.Shipment.Should().BeSameAs(shipment);
        released.TransportOrder.Should().BeSameAs(order);
        released.Release.Should().Be(new ShipmentRelease(Release, ReleasedAt, Key, null));
    }

    [Fact]
    public void Release_ReleasedShipment_ChangesNothing()
    {
        var (order, shipment) = Planned(VanDijk);
        shipment.Release(order, Key, Release, ReleasedAt, BookingNumber);
        shipment.ClearDomainEvents();

        var released = shipment.Release(order, new IdempotencyKey("key-rel2"), new CorrelationId("corr-release-2"), ReleasedAt.AddMinutes(5));

        released.Should().BeNull();
        shipment.LastRelease!.CorrelationId.Should().Be(Release);
        shipment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Release_WithAnotherShipmentsTransportOrder_Throws()
    {
        var (_, shipment) = Planned(VanDijk);
        var (other, _) = Planned(VanDijk, orderSequence: 8);

        var release = () => shipment.Release(other, Key, Release, ReleasedAt);

        release.Should().Throw<ArgumentException>();
        shipment.Status.Should().Be(ShipmentStatus.Planned);
    }

    [Fact]
    public void Reassign_ReleasedShipment_ChangesNothing()
    {
        var (order, shipment) = Planned(VanDijk);
        shipment.Release(order, Key, Release, ReleasedAt, BookingNumber);
        shipment.ClearDomainEvents();

        var reassigned = shipment.Reassign(CarriedBy.OwnFleet, new IdempotencyKey("key-r1"), new CorrelationId("corr-reassign"), ReleasedAt.AddMinutes(5));

        reassigned.Should().BeFalse();
        shipment.CarriedBy.Should().Be(VanDijk);
        shipment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ReleaseUnder_TheReleasesKey_FindsIt_AndAnotherKeyFindsNothing()
    {
        var (order, shipment) = Planned(VanDijk);
        shipment.ReleaseUnder(Key).Should().BeNull();

        shipment.Release(order, Key, Release, ReleasedAt, BookingNumber);

        shipment.ReleaseUnder(new IdempotencyKey("key-rel1")).Should().Be(new ShipmentRelease(Release, ReleasedAt, Key, BookingNumber));
        shipment.ReleaseUnder(new IdempotencyKey("key-rel2")).Should().BeNull();
    }

    [Fact]
    public void CheckRelease_PlannedShipment_RefusesNothing()
    {
        AssignmentRules.CheckRelease(Planned(VanDijk).Shipment).Should().BeNull();
    }

    [Fact]
    public void CheckRelease_ReleasedShipment_IsRefusedAsNotPlanned()
    {
        var (order, shipment) = Planned(VanDijk);
        shipment.Release(order, Key, Release, ReleasedAt, BookingNumber);

        var refusal = AssignmentRules.CheckRelease(shipment);

        refusal!.Code.Should().Be(AssignmentRefusalCode.ShipmentNotPlanned);
        refusal.Message.Should().Be("Shipment SH-0000003 is Released. Only a Planned Shipment can be released.");
    }

    [Fact]
    public void CheckReassignment_ReleasedShipment_IsRefusedAsNotPlanned()
    {
        var (order, shipment) = Planned(VanDijk);
        shipment.Release(order, Key, Release, ReleasedAt, BookingNumber);

        var refusal = AssignmentRules.CheckReassignment(shipment, carrierId: null, carrier: null);

        refusal!.Code.Should().Be(AssignmentRefusalCode.ShipmentNotPlanned);
        refusal.Message.Should().Be("Shipment SH-0000003 is Released. Only a Planned Shipment can be reassigned.");
    }

    private static (TransportOrder Order, Shipment Shipment) Planned(CarriedBy carriedBy, long orderSequence = 7)
    {
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            TransportOrderNumber.FromSequence(orderSequence),
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-intake"),
            ReceivedAt);
        var shipment = order.Plan(ShipmentNumber.FromSequence(3), carriedBy, new IdempotencyKey("key-1"), new CorrelationId("corr-planning"), PlannedAt)!;
        shipment.ClearDomainEvents();
        return (order, shipment);
    }
}
