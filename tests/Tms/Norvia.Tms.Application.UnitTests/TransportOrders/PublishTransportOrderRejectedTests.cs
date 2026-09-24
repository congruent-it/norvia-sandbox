using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class PublishTransportOrderRejectedTests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_RejectedBooking_PublishesTransportOrderRejectedWithItsReasons()
    {
        var order = Samples.RejectedOrder(correlationId: "corr-9");

        await new PublishTransportOrderRejected(_outbox).Handle(new TransportOrderRejectedEvent(order), CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<TransportOrderRejectedV1>(e =>
            e.TransportOrderId == order.Id.Value
            && e.CorrelationId == "corr-9"
            && e.OccurredAt == Samples.Now
            && e.ShipperId == "SHP-0001"
            && e.ShipperReference == "BD-2026-0001"
            && e.Reasons.SequenceEqual(new[]
            {
                new RejectionReasonView("InactiveShipper", "The Shipper is not active, so Norvia can't take bookings for it."),
                new RejectionReasonView("GoodsLinesMissing", "The booking has no goods lines."),
            })));
    }

    [Fact]
    public void TransportOrderRejected_Always_GoesToTheTransportOrderEventsTopicWithTheTrackingKeys()
    {
        var rejected = TransportOrderRejectedV1.From(Samples.RejectedOrder(correlationId: "corr-9"));

        rejected.Topic.Should().Be("transport-order-events");
        rejected.EventType.Should().Be("TransportOrderRejected");
        rejected.EventVersion.Should().Be(1);
        rejected.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "TransportOrderRejected",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-9",
            ["flow"] = "transport-order-intake",
            ["partyId"] = "SHP-0001",
        });
    }
}
