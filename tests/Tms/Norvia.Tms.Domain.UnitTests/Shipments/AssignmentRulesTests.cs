using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

/// <summary>P3's assignment rules: what the TMS refuses before it plans a Transport Order.</summary>
public sealed class AssignmentRulesTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly Carrier ActiveVanDijk = new(VanDijk, "Van Dijk Transport", Active: true);

    [Fact]
    public void Check_AcceptedOrderToOwnFleet_RefusesNothing()
    {
        AssignmentRules.Check(Accepted(), carrierId: null, carrier: null).Should().BeNull();
    }

    [Fact]
    public void Check_AcceptedOrderToAnActiveCarrier_RefusesNothing()
    {
        AssignmentRules.Check(Accepted(), VanDijk, ActiveVanDijk).Should().BeNull();
    }

    [Fact]
    public void Check_RatingFailedOrder_RefusesNothing()
    {
        var order = Accepted();
        order.Rate(null, ReceivedAt.AddSeconds(2));

        AssignmentRules.Check(order, VanDijk, ActiveVanDijk).Should().BeNull("the rating result never blocks planning");
    }

    [Fact]
    public void Check_PlannedOrder_IsRefusedAsAlreadyPlanned()
    {
        var order = Accepted();
        order.Plan(ShipmentNumber.FromSequence(3), CarriedBy.OwnFleet, new IdempotencyKey("key-1"), new CorrelationId("corr-planning"), ReceivedAt);

        var refusal = AssignmentRules.Check(order, VanDijk, ActiveVanDijk);

        refusal!.Code.Should().Be(AssignmentRefusalCode.TransportOrderAlreadyPlanned);
        refusal.Message.Should().Be("Transport Order TO-0000007 is Planned already, as Shipment SH-0000003. A Transport Order has exactly one Shipment.");
    }

    [Fact]
    public void Check_RejectedBooking_IsRefusedAsNotAccepted()
    {
        var booking = TransportOrder.Reject(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            [new RejectionReason(RejectionCode.InactiveShipper, "Inactive.")],
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-intake"),
            ReceivedAt);

        var refusal = AssignmentRules.Check(booking, carrierId: null, carrier: null);

        refusal!.Code.Should().Be(AssignmentRefusalCode.TransportOrderNotAccepted);
        refusal.Message.Should().Be("The Transport Order is Rejected. Only an Accepted Transport Order can be assigned.");
    }

    [Fact]
    public void Check_UnknownCarrier_IsRefusedAsUnknownCarrier()
    {
        var refusal = AssignmentRules.Check(Accepted(), new CarrierId("CAR-9999"), carrier: null);

        refusal!.Code.Should().Be(AssignmentRefusalCode.UnknownCarrier);
        refusal.Message.Should().Be("Carrier CAR-9999 is not known to the TMS.");
    }

    [Fact]
    public void Check_InactiveCarrier_IsRefusedAsInactiveCarrier()
    {
        var refusal = AssignmentRules.Check(Accepted(), VanDijk, ActiveVanDijk with { Active = false });

        refusal!.Code.Should().Be(AssignmentRefusalCode.InactiveCarrier);
        refusal.Message.Should().Be("Carrier CAR-0001 (Van Dijk Transport) is not active, so it can't be chosen.");
    }

    [Fact]
    public void Check_PlannedOrderToAnUnknownCarrier_IsRefusedForTheOrderFirst()
    {
        var order = Accepted();
        order.Plan(ShipmentNumber.FromSequence(3), CarriedBy.OwnFleet, new IdempotencyKey("key-1"), new CorrelationId("corr-planning"), ReceivedAt);

        AssignmentRules.Check(order, new CarrierId("CAR-9999"), carrier: null)!.Code
            .Should().Be(AssignmentRefusalCode.TransportOrderAlreadyPlanned);
    }

    [Fact]
    public void TransportOrderNotFound_NamesTheNumber()
    {
        var refusal = AssignmentRules.TransportOrderNotFound("TO-0000099");

        refusal.Code.Should().Be(AssignmentRefusalCode.TransportOrderNotFound);
        refusal.Message.Should().Be("Transport Order TO-0000099 is not known to the TMS.");
    }

    private static TransportOrder Accepted() => TransportOrder.Accept(
        new ShipperId("SHP-0001"),
        TransportOrderTests.SampleDetails(),
        TransportOrderNumber.FromSequence(7),
        IntakeChannel.ShipperApi,
        new CorrelationId("corr-intake"),
        ReceivedAt);
}
