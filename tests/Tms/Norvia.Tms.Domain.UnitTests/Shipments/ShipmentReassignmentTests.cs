using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

/// <summary>
/// While a Shipment is Planned, the Planner can fix a mistake by assigning it to Own fleet or another Carrier (P3). The
/// Shipment keeps its Shipment number and records each assignment in its history.
/// </summary>
public sealed class ShipmentReassignmentTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PlannedAt = ReceivedAt.AddHours(2);
    private static readonly DateTimeOffset ReassignedAt = PlannedAt.AddMinutes(20);
    private static readonly CarriedBy VanDijk = CarriedBy.Carrier(new CarrierId("CAR-0001"));
    private static readonly CarriedBy Kessler = CarriedBy.Carrier(new CarrierId("CAR-0002"));
    private static readonly CorrelationId Reassignment = new("corr-reassign");
    private static readonly IdempotencyKey Key = new("key-r1");

    [Fact]
    public void Plan_AcceptedOrder_StartsTheHistoryWithThePlanningsAssignment()
    {
        var shipment = Planned(VanDijk);

        shipment.Assignments.Should().Equal(new ShipmentAssignment(VanDijk, new CorrelationId("corr-planning"), PlannedAt));
    }

    [Fact]
    public void Reassign_PlannedShipmentToAnotherCarrier_CarriesItWithThatCarrier_AndKeepsTheShipmentNumber()
    {
        var shipment = Planned(VanDijk);

        var reassigned = shipment.Reassign(Kessler, Key, Reassignment, ReassignedAt);

        reassigned.Should().BeTrue();
        shipment.CarriedBy.Should().Be(Kessler);
        shipment.Number.Should().Be(ShipmentNumber.FromSequence(3));
        shipment.Status.Should().Be(ShipmentStatus.Planned);
    }

    [Fact]
    public void Reassign_PlannedShipment_AddsTheReassignmentToTheHistory_InOrder()
    {
        var shipment = Planned(VanDijk);

        shipment.Reassign(CarriedBy.OwnFleet, Key, Reassignment, ReassignedAt);
        shipment.Reassign(Kessler, new IdempotencyKey("key-r2"), new CorrelationId("corr-reassign-2"), ReassignedAt.AddMinutes(5));

        shipment.Assignments.Should().Equal(
            new ShipmentAssignment(VanDijk, new CorrelationId("corr-planning"), PlannedAt),
            new ShipmentAssignment(CarriedBy.OwnFleet, Reassignment, ReassignedAt, Key),
            new ShipmentAssignment(Kessler, new CorrelationId("corr-reassign-2"), ReassignedAt.AddMinutes(5), new IdempotencyKey("key-r2")));
    }

    [Fact]
    public void Reassign_PlannedShipment_KeepsThePlanningsTrail()
    {
        var shipment = Planned(VanDijk);

        shipment.Reassign(Kessler, Key, Reassignment, ReassignedAt);

        shipment.CorrelationId.Should().Be(new CorrelationId("corr-planning"), "each reassignment has its own trail, in the history");
        shipment.PlannedAt.Should().Be(PlannedAt);
    }

    [Fact]
    public void Reassign_PlannedShipment_RaisesShipmentReassignedWithWhoCarriedItBefore()
    {
        var shipment = Planned(VanDijk);

        shipment.Reassign(CarriedBy.OwnFleet, Key, Reassignment, ReassignedAt);

        var reassigned = shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentReassignedEvent>().Subject;
        reassigned.Shipment.Should().BeSameAs(shipment);
        reassigned.Previous.CarriedBy.Should().Be(VanDijk);
        reassigned.Reassignment.Should().Be(new ShipmentAssignment(CarriedBy.OwnFleet, Reassignment, ReassignedAt, Key));
    }

    [Fact]
    public void Reassign_ToWhoCarriesItAlready_ChangesNothing()
    {
        var shipment = Planned(VanDijk);

        var reassigned = shipment.Reassign(CarriedBy.Carrier(new CarrierId("CAR-0001")), Key, Reassignment, ReassignedAt);

        reassigned.Should().BeFalse();
        shipment.Assignments.Should().ContainSingle();
        shipment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ReassignmentUnder_TheKeyOfAReassignment_FindsIt_AndItsPredecessor()
    {
        var shipment = Planned(VanDijk);
        shipment.Reassign(Kessler, Key, Reassignment, ReassignedAt);
        shipment.Reassign(CarriedBy.OwnFleet, new IdempotencyKey("key-r2"), new CorrelationId("corr-reassign-2"), ReassignedAt.AddMinutes(5));

        var found = shipment.ReassignmentUnder(new IdempotencyKey("key-r1"));

        found!.Value.Reassignment.CorrelationId.Should().Be(Reassignment);
        found.Value.Previous.CarriedBy.Should().Be(VanDijk);
    }

    [Fact]
    public void ReassignmentUnder_AnUnknownKey_FindsNothing()
    {
        Planned(VanDijk).ReassignmentUnder(Key).Should().BeNull();
    }

    [Fact]
    public void CheckReassignment_PlannedShipmentToAnActiveCarrier_RefusesNothing()
    {
        AssignmentRules.CheckReassignment(Planned(VanDijk), Kessler.CarrierId, new Carrier(Kessler.CarrierId!, "Kessler Spedition", Active: true))
            .Should().BeNull();
    }

    [Fact]
    public void CheckReassignment_PlannedShipmentToOwnFleet_RefusesNothing()
    {
        AssignmentRules.CheckReassignment(Planned(VanDijk), carrierId: null, carrier: null).Should().BeNull();
    }

    [Fact]
    public void CheckReassignment_UnknownCarrier_IsRefusedWithP32sCode()
    {
        var refusal = AssignmentRules.CheckReassignment(Planned(VanDijk), new CarrierId("CAR-9999"), carrier: null);

        refusal!.Code.Should().Be(AssignmentRefusalCode.UnknownCarrier);
        refusal.Message.Should().Be("Carrier CAR-9999 is not known to the TMS.");
    }

    [Fact]
    public void CheckReassignment_InactiveCarrier_IsRefusedWithP32sCode()
    {
        var refusal = AssignmentRules.CheckReassignment(Planned(CarriedBy.OwnFleet), Kessler.CarrierId, new Carrier(Kessler.CarrierId!, "Kessler Spedition", Active: false));

        refusal!.Code.Should().Be(AssignmentRefusalCode.InactiveCarrier);
    }

    [Fact]
    public void CheckReassignment_ToWhoCarriesItAlready_IsRefusedAsUnchanged()
    {
        var refusal = AssignmentRules.CheckReassignment(Planned(CarriedBy.OwnFleet), carrierId: null, carrier: null);

        refusal!.Code.Should().Be(AssignmentRefusalCode.AssignmentUnchanged);
        refusal.Message.Should().Be("Shipment SH-0000003 is assigned to Own fleet already.");
    }

    [Fact]
    public void CheckReassignment_ToTheCarrierThatCarriesItAlready_IsRefusedAsUnchanged_NamingTheCarrier()
    {
        var refusal = AssignmentRules.CheckReassignment(
            Planned(VanDijk), VanDijk.CarrierId, new Carrier(VanDijk.CarrierId!, "Van Dijk Transport", Active: true));

        refusal!.Code.Should().Be(AssignmentRefusalCode.AssignmentUnchanged);
        refusal.Message.Should().Be("Shipment SH-0000003 is assigned to Carrier CAR-0001 (Van Dijk Transport) already.");
    }

    [Fact]
    public void ShipmentNotFound_NamesTheNumber()
    {
        var refusal = AssignmentRules.ShipmentNotFound("SH-0000099");

        refusal.Code.Should().Be(AssignmentRefusalCode.ShipmentNotFound);
        refusal.Message.Should().Be("Shipment SH-0000099 is not known to the TMS.");
    }

    private static Shipment Planned(CarriedBy carriedBy)
    {
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            TransportOrderNumber.FromSequence(7),
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-intake"),
            ReceivedAt);
        var shipment = order.Plan(ShipmentNumber.FromSequence(3), carriedBy, new IdempotencyKey("key-1"), new CorrelationId("corr-planning"), PlannedAt)!;
        shipment.ClearDomainEvents();
        return shipment;
    }
}
