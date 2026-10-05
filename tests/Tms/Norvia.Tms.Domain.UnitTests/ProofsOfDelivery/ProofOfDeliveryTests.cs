using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.ProofsOfDelivery;

public sealed class ProofOfDeliveryTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 26, 8, 0, 0, TimeSpan.Zero);
    private static readonly CarrierId VanDijk = new("CAR-0001");

    [Fact]
    public void Record_MakesTheShipmentsCurrentProofOfDelivery_AndRaisesProofOfDeliveryReceived()
    {
        var shipment = BookedShipment();

        var proofOfDelivery = ProofOfDelivery.Record(shipment, Report(), new CorrelationId("corr-pod"), ReceivedAt);

        proofOfDelivery.ShipmentId.Should().Be(shipment.Id);
        proofOfDelivery.ShipmentNumber.Should().Be(shipment.Number);
        proofOfDelivery.IsCurrent.Should().BeTrue();
        proofOfDelivery.ReceivedBy.Should().Be("J. Becker");
        proofOfDelivery.Files.Should().ContainSingle().Which.Name.Should().Be("cmr.pdf");
        proofOfDelivery.CorrelationId.Value.Should().Be("corr-pod");
        proofOfDelivery.ReceivedAt.Should().Be(ReceivedAt);
        var received = proofOfDelivery.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProofOfDeliveryReceivedEvent>().Subject;
        received.ProofOfDelivery.Should().BeSameAs(proofOfDelivery);
        received.Shipment.Should().BeSameAs(shipment);
        received.ReplacesEarlier.Should().BeFalse();
    }

    [Fact]
    public void Record_NeverChangesTheShipment()
    {
        var shipment = BookedShipment();

        ProofOfDelivery.Record(shipment, Report(), new CorrelationId("corr-pod"), ReceivedAt);

        shipment.Status.Should().Be(ShipmentStatus.Booked);
        shipment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Record_ReplacingTheCurrentOne_SupersedesIt_AndSaysSoInTheEvent()
    {
        var shipment = BookedShipment();
        var earlier = ProofOfDelivery.Record(shipment, Report(), new CorrelationId("corr-old"), ReceivedAt);

        var later = ProofOfDelivery.Record(shipment, Report(), new CorrelationId("corr-new"), ReceivedAt.AddHours(1), replaces: earlier);

        earlier.IsCurrent.Should().BeFalse();
        later.IsCurrent.Should().BeTrue();
        later.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProofOfDeliveryReceivedEvent>().Which.ReplacesEarlier.Should().BeTrue();
        later.CorrelationId.Value.Should().Be("corr-new");
    }

    [Fact]
    public void Record_ReplacingASupersededOneOrAnotherShipmentsOne_IsABug()
    {
        var shipment = BookedShipment();
        var superseded = ProofOfDelivery.Record(shipment, Report(), new CorrelationId("corr-old"), ReceivedAt);
        ProofOfDelivery.Record(shipment, Report(), new CorrelationId("corr-new"), ReceivedAt, replaces: superseded);
        var ofAnother = ProofOfDelivery.Record(BookedShipment(sequence: 13), Report(), new CorrelationId("corr-other"), ReceivedAt);

        var replaceSuperseded = () => ProofOfDelivery.Record(shipment, Report(), new CorrelationId("corr-x"), ReceivedAt, replaces: superseded);
        var replaceAnothers = () => ProofOfDelivery.Record(shipment, Report(), new CorrelationId("corr-x"), ReceivedAt, replaces: ofAnother);

        replaceSuperseded.Should().Throw<ArgumentException>();
        replaceAnothers.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_WithNoFiles_IsABug()
    {
        var act = () => ProofOfDelivery.Record(BookedShipment(), Report(files: []), new CorrelationId("corr-pod"), ReceivedAt);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CurrentProofOfDeliverySpec_FindsOnlyTheShipmentsCurrentOne()
    {
        var shipment = BookedShipment();
        var other = BookedShipment(sequence: 13);
        var current = ProofOfDelivery.Record(shipment, Report(), new CorrelationId("corr-pod"), ReceivedAt);
        var forAnother = ProofOfDelivery.Record(other, Report(), new CorrelationId("corr-other"), ReceivedAt);

        var found = new CurrentProofOfDeliverySpec(shipment.Id).Evaluate([forAnother, current]);

        found.Should().ContainSingle().Which.Should().BeSameAs(current);
    }

    [Fact]
    public void RulesCheckShipment_RefusesAShipmentThatIsNotBookedYet_AndAcceptsTheRest()
    {
        var planned = PlannedShipment();

        ProofOfDeliveryRules.CheckShipment(planned)!.Code.Should().Be(ProofOfDeliveryRefusalCode.ShipmentNotInTransport);
        ProofOfDeliveryRules.CheckShipment(BookedShipment()).Should().BeNull();
    }

    [Fact]
    public void RulesShipmentNotFound_NamesTheReference()
    {
        var refusal = ProofOfDeliveryRules.ShipmentNotFound("VDT-9");

        refusal.Code.Should().Be(ProofOfDeliveryRefusalCode.ShipmentNotFound);
        refusal.Message.Should().Contain("VDT-9");
    }

    [Fact]
    public void Source_IsACarrierOrOwnFleet()
    {
        ProofOfDeliverySource.Carrier(VanDijk).CarrierId.Should().Be(VanDijk);
        ProofOfDeliverySource.Carrier(VanDijk).ToString().Should().Be("CAR-0001");
        ProofOfDeliverySource.OwnFleet.IsOwnFleet.Should().BeTrue();
        ProofOfDeliverySource.OwnFleet.ToString().Should().Be("Own fleet");
    }

    private static Shipment BookedShipment(long sequence = 3)
    {
        var (order, shipment) = PlannedWithOrder(sequence);
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), ReceivedAt.AddHours(3), CarrierBookingNumber.FromSequence(sequence));
        shipment.Book();
        shipment.ClearDomainEvents();
        return shipment;
    }

    private static Shipment PlannedShipment() => PlannedWithOrder(3).Shipment;

    private static (TransportOrder Order, Shipment Shipment) PlannedWithOrder(long sequence)
    {
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            TransportOrderNumber.FromSequence(sequence),
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-intake"),
            ReceivedAt);
        var shipment = order.Plan(
            ShipmentNumber.FromSequence(sequence),
            CarriedBy.Carrier(VanDijk),
            new IdempotencyKey("key-1"),
            new CorrelationId("corr-planning"),
            ReceivedAt.AddHours(2))!;
        shipment.ClearDomainEvents();
        return (order, shipment);
    }

    private static ProofOfDeliveryReport Report(IReadOnlyList<ProofOfDeliveryFile>? files = null) =>
        new(
            ProofOfDeliverySource.Carrier(VanDijk),
            "doc-1",
            "VDT-1",
            "J. Becker",
            new DateTimeOffset(2026, 9, 25, 14, 30, 0, TimeSpan.FromHours(2)),
            new TrackingLocation("Düsseldorf", "40213", "DE"),
            DeliveryCondition.Clean,
            null,
            files ?? [ProofOfDeliveryFile.New("cmr.pdf", "application/pdf", 4)]);
}
