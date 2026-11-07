using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class TransportOrderBillingTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DeliveredAt = ReceivedAt.AddDays(2);
    private static readonly DateTimeOffset BillableAt = DeliveredAt.AddHours(1);
    private static readonly CorrelationId BillingCorrelationId = new("billing-corr");

    [Fact]
    public void Accept_ShipperApiBooking_IsNotYetBillable()
    {
        Accepted(IntakeChannel.ShipperApi).BillingState.Should().Be(BillingState.NotYetBillable);
    }

    [Fact]
    public void Accept_WmsHandoff_IsNotBilledSeparately()
    {
        Accepted(IntakeChannel.Wms).BillingState.Should().Be(BillingState.NotBilledSeparately);
    }

    [Fact]
    public void Reject_Booking_HasNoBillingState()
    {
        var order = TransportOrder.Reject(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            [new(RejectionCode.GoodsLinesMissing, "No goods.")],
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-1"),
            ReceivedAt);

        order.BillingState.Should().BeNull();
    }

    [Fact]
    public void TryBecomeBillable_DeliveredRatedOrderWithAProofOfDelivery_BecomesBillableAndRaisesTransportOrderBillable()
    {
        var order = PlannedRated();

        var billable = order.TryBecomeBillable(new BillingFacts(DeliveredAt, HasCurrentProofOfDelivery: true, HasOpenException: false), BillingCorrelationId, BillableAt);

        billable.Should().BeTrue();
        order.BillingState.Should().Be(BillingState.Billable);
        order.DeliveredAt.Should().Be(DeliveredAt);
        order.BecameBillableAt.Should().Be(BillableAt);
        order.BillingCorrelationId.Should().Be(BillingCorrelationId);
        order.ReleasedWithoutProofOfDelivery.Should().BeFalse();
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransportOrderBillableEvent>()
            .Which.TransportOrder.Should().BeSameAs(order);
    }

    [Fact]
    public void TryBecomeBillable_ShipmentWithAnOpenException_StaysNotYetBillable()
    {
        var order = PlannedRated();

        var billable = order.TryBecomeBillable(new BillingFacts(DeliveredAt, HasCurrentProofOfDelivery: true, HasOpenException: true), BillingCorrelationId, BillableAt);

        billable.Should().BeFalse();
        order.BillingState.Should().Be(BillingState.NotYetBillable);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void TryBecomeBillable_ShipmentNotDelivered_StaysNotYetBillable()
    {
        var order = PlannedRated();

        var billable = order.TryBecomeBillable(new BillingFacts(null, HasCurrentProofOfDelivery: true, HasOpenException: false), BillingCorrelationId, BillableAt);

        billable.Should().BeFalse();
        order.BillingState.Should().Be(BillingState.NotYetBillable);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void TryBecomeBillable_NoCurrentProofOfDelivery_StaysNotYetBillable()
    {
        var order = PlannedRated();

        var billable = order.TryBecomeBillable(new BillingFacts(DeliveredAt, HasCurrentProofOfDelivery: false, HasOpenException: false), BillingCorrelationId, BillableAt);

        billable.Should().BeFalse();
        order.BillingState.Should().Be(BillingState.NotYetBillable);
        order.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryBecomeBillable_OrderNotRated_StaysNotYetBillable(bool ratingFailed)
    {
        var order = Planned();
        if (ratingFailed)
        {
            order.Rate(null, ReceivedAt.AddSeconds(2));
        }

        order.ClearDomainEvents();

        var billable = order.TryBecomeBillable(new BillingFacts(DeliveredAt, true, HasOpenException: false), BillingCorrelationId, BillableAt);

        billable.Should().BeFalse();
        order.BillingState.Should().Be(BillingState.NotYetBillable);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void TryBecomeBillable_OrderRatedAfterRatingFailed_BecomesBillable()
    {
        var order = Planned();
        order.Rate(null, ReceivedAt.AddSeconds(2));
        order.TryBecomeBillable(new BillingFacts(DeliveredAt, true, HasOpenException: false), BillingCorrelationId, BillableAt).Should().BeFalse();

        order.ReRate(RateCard(), BillableAt, new IdempotencyKey("key-1"));
        var billable = order.TryBecomeBillable(new BillingFacts(DeliveredAt, true, HasOpenException: false), new CorrelationId("re-rate-corr"), BillableAt);

        billable.Should().BeTrue();
        order.BillingCorrelationId.Should().Be(new CorrelationId("re-rate-corr"));
    }

    [Fact]
    public void TryBecomeBillable_FulfilmentOrder_NeverBecomesBillable()
    {
        var order = Accepted(IntakeChannel.Wms);
        order.Plan(ShipmentNumber.FromSequence(3), CarriedBy.OwnFleet, new IdempotencyKey("plan"), new CorrelationId("plan-corr"), ReceivedAt.AddHours(1));
        order.ClearDomainEvents();

        var billable = order.TryBecomeBillable(new BillingFacts(DeliveredAt, true, HasOpenException: false), BillingCorrelationId, BillableAt);

        billable.Should().BeFalse();
        order.BillingState.Should().Be(BillingState.NotBilledSeparately);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void TryBecomeBillable_AlreadyBillable_ChangesNothingAndRaisesNothing()
    {
        var order = PlannedRated();
        order.TryBecomeBillable(new BillingFacts(DeliveredAt, true, HasOpenException: false), BillingCorrelationId, BillableAt);
        order.ClearDomainEvents();

        var again = order.TryBecomeBillable(new BillingFacts(DeliveredAt.AddDays(1), true, HasOpenException: false), new CorrelationId("later"), BillableAt.AddDays(1));

        again.Should().BeFalse("Billable is final: nothing publishes the event again");
        order.BillingState.Should().Be(BillingState.Billable);
        order.DeliveredAt.Should().Be(DeliveredAt);
        order.BecameBillableAt.Should().Be(BillableAt);
        order.BillingCorrelationId.Should().Be(BillingCorrelationId);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void TryBecomeBillable_OrderNotPlanned_StaysNotYetBillable()
    {
        var order = Accepted(IntakeChannel.ShipperApi);
        order.Rate(RateCard(), ReceivedAt.AddSeconds(2));
        order.ClearDomainEvents();

        var billable = order.TryBecomeBillable(new BillingFacts(DeliveredAt, true, HasOpenException: false), BillingCorrelationId, BillableAt);

        billable.Should().BeFalse("an order with no Shipment can't have been delivered");
        order.BillingState.Should().Be(BillingState.NotYetBillable);
    }

    [Fact]
    public void ReleaseWithoutProofOfDelivery_DeliveredOrder_RecordsWhoWhenAndWhyAndPublishesNothing()
    {
        var order = PlannedRated();

        var refusal = order.ReleaseWithoutProofOfDelivery(DeliveredAt, "CMR received by post", "Jan", BillableAt);

        refusal.Should().BeNull();
        order.ReleasedWithoutProofOfDelivery.Should().BeTrue();
        order.ReleasedBy.Should().Be("Jan");
        order.ReleasedAt.Should().Be(BillableAt);
        order.ReleaseRemark.Should().Be("CMR received by post");
        order.BillingState.Should().Be(BillingState.NotYetBillable);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ReleaseRefusal_DeliveredOrderNotReleasedYet_IsNullAndChangesNothing()
    {
        var order = PlannedRated();

        order.ReleaseRefusal(DeliveredAt).Should().BeNull();

        order.ReleasedWithoutProofOfDelivery.Should().BeFalse();
    }

    [Fact]
    public void ReleaseRefusal_IsTheRuleTheReleaseApplies_ForNotDeliveredAndAlreadyReleased()
    {
        var order = PlannedRated();
        order.ReleaseRefusal(null)!.Code.Should().Be(order.ReleaseWithoutProofOfDelivery(null, "CMR", "Jan", BillableAt)!.Code).And.Be(BillingRefusalCode.ShipmentNotDelivered);

        order.ReleaseWithoutProofOfDelivery(DeliveredAt, "CMR", "Jan", BillableAt);

        order.ReleaseRefusal(DeliveredAt)!.Code.Should().Be(BillingRefusalCode.AlreadyReleased);
    }

    [Fact]
    public void TryBecomeBillable_ReleasedWithoutProofOfDelivery_BecomesBillable()
    {
        var order = PlannedRated();
        order.ReleaseWithoutProofOfDelivery(DeliveredAt, "CMR by post", "Jan", BillableAt);

        var billable = order.TryBecomeBillable(new BillingFacts(DeliveredAt, HasCurrentProofOfDelivery: false, HasOpenException: false), BillingCorrelationId, BillableAt);

        billable.Should().BeTrue();
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TransportOrderBillableEvent>();
    }

    [Fact]
    public void TryBecomeBillable_ReleasedWithoutProofOfDeliveryButOpenException_StaysNotYetBillable()
    {
        var order = PlannedRated();
        order.ReleaseWithoutProofOfDelivery(DeliveredAt, "CMR by post", "Jan", BillableAt);

        order.TryBecomeBillable(new BillingFacts(DeliveredAt, false, HasOpenException: true), BillingCorrelationId, BillableAt).Should().BeFalse();
    }

    [Fact]
    public void ReleaseWithoutProofOfDelivery_UnratedOrder_IsAllowedAndKeepsItNotYetBillable()
    {
        var order = Planned();

        order.ReleaseWithoutProofOfDelivery(DeliveredAt, "CMR by post", "Jan", BillableAt).Should().BeNull();

        order.TryBecomeBillable(new BillingFacts(DeliveredAt, false, false), BillingCorrelationId, BillableAt).Should().BeFalse();
        order.BillingState.Should().Be(BillingState.NotYetBillable);
    }

    [Fact]
    public void ReleaseWithoutProofOfDelivery_ShipmentNotDelivered_IsRefused()
    {
        var order = PlannedRated();

        order.ReleaseWithoutProofOfDelivery(null, "CMR by post", "Jan", BillableAt)!.Code.Should().Be(BillingRefusalCode.ShipmentNotDelivered);

        order.ReleasedWithoutProofOfDelivery.Should().BeFalse();
    }

    [Fact]
    public void ReleaseWithoutProofOfDelivery_BillableOrder_IsRefused()
    {
        var order = PlannedRated();
        order.TryBecomeBillable(new BillingFacts(DeliveredAt, true, false), BillingCorrelationId, BillableAt);

        order.ReleaseWithoutProofOfDelivery(DeliveredAt, "CMR by post", "Jan", BillableAt)!.Code.Should().Be(BillingRefusalCode.AlreadyBillable);

        order.ReleasedWithoutProofOfDelivery.Should().BeFalse();
    }

    [Fact]
    public void ReleaseWithoutProofOfDelivery_FulfilmentOrder_IsRefused()
    {
        var order = Accepted(IntakeChannel.Wms);

        order.ReleaseWithoutProofOfDelivery(DeliveredAt, "CMR by post", "Jan", BillableAt)!.Code.Should().Be(BillingRefusalCode.NotBilledSeparately);
    }

    [Fact]
    public void ReleaseWithoutProofOfDelivery_ReleasedAlready_IsRefusedAndKeepsTheFirstRelease()
    {
        var order = Planned();
        order.ReleaseWithoutProofOfDelivery(DeliveredAt, "first", "Jan", BillableAt);

        order.ReleaseWithoutProofOfDelivery(DeliveredAt, "second", "Piet", BillableAt.AddHours(1))!.Code.Should().Be(BillingRefusalCode.AlreadyReleased);

        order.ReleaseRemark.Should().Be("first");
        order.ReleasedBy.Should().Be("Jan");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ReleaseWithoutProofOfDelivery_BlankRemark_Throws(string remark)
    {
        var release = () => PlannedRated().ReleaseWithoutProofOfDelivery(DeliveredAt, remark, "Jan", BillableAt);

        release.Should().Throw<ArgumentException>();
    }

    private static RateCard RateCard() => new(
        new RateCardId("RC-0001"),
        new ShipperId("SHP-0001"),
        [
            new RateCardVersion(
                1,
                new DateOnly(2026, 1, 1),
                null,
                [new Zone(new ZoneName("Rotterdam"), "NL", ["30"]), new Zone(new ZoneName("Utrecht"), "NL", ["35"])],
                [new Lane(new ZoneName("Rotterdam"), new ZoneName("Utrecht"), [new WeightBreak(1_000m, new Money(12.37m))], new Money(45.00m))]),
        ]);

    private static TransportOrder Planned()
    {
        var order = Accepted(IntakeChannel.ShipperApi);
        order.Plan(ShipmentNumber.FromSequence(3), CarriedBy.OwnFleet, new IdempotencyKey("plan"), new CorrelationId("plan-corr"), ReceivedAt.AddHours(1));
        order.ClearDomainEvents();
        return order;
    }

    private static TransportOrder PlannedRated()
    {
        var order = Planned();
        order.Rate(RateCard(), ReceivedAt.AddSeconds(2));
        order.ClearDomainEvents();
        return order;
    }

    private static TransportOrder Accepted(IntakeChannel channel) =>
        TransportOrder.Accept(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            TransportOrderNumber.FromSequence(7),
            channel,
            new CorrelationId("corr-1"),
            ReceivedAt);
}
