using Ardalis.SharedKernel;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Billing;

public sealed class PublishTransportOrderBillableTests
{
    private static readonly DateTimeOffset DeliveredAt = Samples.Now.AddDays(2);
    private static readonly DateTimeOffset BillableAt = Samples.Now.AddDays(3);

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_BillableOrder_PublishesTransportOrderBillableWithEverythingTheErpNeeds()
    {
        var order = BillableOrder();
        var expectedCharge = new FreightChargeView("EUR", 112.90m, [new ChargeLineView("Freight", 112.90m)], new RateCardVersionView("RC-0001", 3));

        await new PublishTransportOrderBillable(_outbox).Handle(new TransportOrderBillableEvent(order), CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<TransportOrderBillableV1>(e =>
            e.TransportOrderNumber == "TO-0000003"
            && e.ShipperId == "SHP-0001"
            && e.ShipperReference == "BD-2026-0001"
            && e.ShipmentNumber == "SH-0000012"
            && e.DeliveryTime == DeliveredAt
            && e.FreightCharge == expectedCharge
            && !e.ReleasedWithoutProofOfDelivery
            && e.IntakeCorrelationId == "corr-intake"
            && e.CorrelationId == "corr-billing"
            && e.OccurredAt == BillableAt));
    }

    [Fact]
    public void TransportOrderBillable_Always_GoesToTheTransportOrderEventsTopicWithTheTrackingKeys()
    {
        var billable = TransportOrderBillableV1.From(BillableOrder());

        billable.Topic.Should().Be("transport-order-events");
        billable.EventType.Should().Be("TransportOrderBillable");
        billable.EventVersion.Should().Be(1);
        billable.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "TransportOrderBillable",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-billing",
            ["flow"] = "billing",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000003",
            ["shipmentNumber"] = "SH-0000012",
        });
    }

    [Fact]
    public void From_OrderNotBillable_Throws()
    {
        var act = () => TransportOrderBillableV1.From(Samples.RatedOrder());

        act.Should().Throw<ArgumentException>();
    }

    private static TransportOrder BillableOrder()
    {
        var order = Samples.AcceptedOrder(sequence: 3, correlationId: "corr-intake");
        order.Rate(Samples.RateCard(), Samples.Now.AddSeconds(2));
        order.Plan(ShipmentNumber.FromSequence(12), CarriedBy.OwnFleet, new IdempotencyKey("plan"), new CorrelationId("corr-plan"), Samples.Now.AddHours(1));
        order.TryBecomeBillable(new BillingFacts(DeliveredAt, HasCurrentProofOfDelivery: true), new CorrelationId("corr-billing"), BillableAt);
        order.ClearDomainEvents();
        return order;
    }
}
