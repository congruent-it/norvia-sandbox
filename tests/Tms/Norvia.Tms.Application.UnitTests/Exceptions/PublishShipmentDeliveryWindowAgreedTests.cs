using Norvia.Tms.Application.Exceptions.AgreeDeliveryWindow;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class PublishShipmentDeliveryWindowAgreedTests
{
    private static readonly DateTimeOffset Earliest = Samples.Now.AddDays(4);

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_PublishesShipmentDeliveryWindowAgreed_OnTheAgreementsTrail()
    {
        var raised = Agreed();

        await new PublishShipmentDeliveryWindowAgreed(_outbox).Handle(raised, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ShipmentDeliveryWindowAgreedV1>(e =>
            e.EventType == "ShipmentDeliveryWindowAgreed"
            && e.ShipmentNumber == "SH-0000012"
            && e.TransportOrderNumber == "TO-0000007"
            && e.Earliest == Earliest
            && e.Latest == Earliest.AddHours(4)
            && e.ExceptionNumber == "EX-0000005"
            && e.CorrelationId == "corr-agree"
            && e.OccurredAt == Samples.Now.AddDays(3)));
    }

    [Fact]
    public void ShipmentDeliveryWindowAgreed_IsOnTheShipmentEventsTopic_TracedOnTheExceptionHandlingFlow()
    {
        var agreed = ShipmentDeliveryWindowAgreedV1.From(Agreed());

        agreed.Topic.Should().Be("shipment-events");
        agreed.EventVersion.Should().Be(1);
        agreed.ApplicationProperties.Should().Contain("flow", "exception-handling").And.Contain("shipmentNumber", "SH-0000012").And.Contain("correlationId", "corr-agree");
    }

    private static ShipmentDeliveryWindowAgreedEvent Agreed()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12);
        var window = new AgreedDeliveryWindow(Earliest, Earliest.AddHours(4), ExceptionNumber.FromSequence(5), new CorrelationId("corr-agree"), Samples.Now.AddDays(3), new IdempotencyKey("key-agree"));
        return new ShipmentDeliveryWindowAgreedEvent(shipment, window);
    }
}
