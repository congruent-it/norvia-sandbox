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

    [Fact]
    public void Release_PlannedShipment_MakesItReleased_AndRecordsTheRelease()
    {
        var (order, shipment) = Planned(VanDijk);

        var released = shipment.Release(order, Key, Release, ReleasedAt);

        released.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Released);
        shipment.LastRelease.Should().Be(new ShipmentRelease(Release, ReleasedAt, Key));
        shipment.CarriedBy.Should().Be(VanDijk);
        shipment.CorrelationId.Should().Be(new CorrelationId("corr-planning"), "the release has its own trail, in its record");
    }

    [Fact]
    public void Release_PlannedShipment_RaisesShipmentReleasedWithItsTransportOrder()
    {
        var (order, shipment) = Planned(CarriedBy.OwnFleet);

        shipment.Release(order, Key, Release, ReleasedAt);

        var released = shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentReleasedEvent>().Subject;
        released.Shipment.Should().BeSameAs(shipment);
        released.TransportOrder.Should().BeSameAs(order);
        released.Release.Should().Be(new ShipmentRelease(Release, ReleasedAt, Key));
    }

    [Fact]
    public void Release_ReleasedShipment_ChangesNothing()
    {
        var (order, shipment) = Planned(VanDijk);
        shipment.Release(order, Key, Release, ReleasedAt);
        shipment.ClearDomainEvents();

        var released = shipment.Release(order, new IdempotencyKey("key-rel2"), new CorrelationId("corr-release-2"), ReleasedAt.AddMinutes(5));

        released.Should().BeFalse();
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
        shipment.Release(order, Key, Release, ReleasedAt);
        shipment.ClearDomainEvents();

        var reassigned = shipment.Reassign(CarriedBy.OwnFleet, new IdempotencyKey("key-r1"), new CorrelationId("corr-reassign"), ReleasedAt.AddMinutes(5));

        reassigned.Should().BeFalse();
        shipment.CarriedBy.Should().Be(VanDijk);
        shipment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void WasReleasedUnder_TheReleasesKey_IsTrue_AndAnotherKeyIsFalse()
    {
        var (order, shipment) = Planned(VanDijk);
        shipment.WasReleasedUnder(Key).Should().BeFalse();

        shipment.Release(order, Key, Release, ReleasedAt);

        shipment.WasReleasedUnder(new IdempotencyKey("key-rel1")).Should().BeTrue();
        shipment.WasReleasedUnder(new IdempotencyKey("key-rel2")).Should().BeFalse();
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
        shipment.Release(order, Key, Release, ReleasedAt);

        var refusal = AssignmentRules.CheckRelease(shipment);

        refusal!.Code.Should().Be(AssignmentRefusalCode.ShipmentNotPlanned);
        refusal.Message.Should().Be("Shipment SH-0000003 is Released. Only a Planned Shipment can be released.");
    }

    [Fact]
    public void CheckReassignment_ReleasedShipment_IsRefusedAsNotPlanned()
    {
        var (order, shipment) = Planned(VanDijk);
        shipment.Release(order, Key, Release, ReleasedAt);

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
