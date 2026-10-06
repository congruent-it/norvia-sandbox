using Norvia.Tms.Application.Exceptions.Open;
using Norvia.Tms.Application.Exceptions.Resolve;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class PublishExceptionEventsTests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_ExceptionOpened_PublishesExceptionOpened_WithTheCausesCorrelationId()
    {
        var exception = OpenExceptions.DeliveryFailed();
        var opened = exception.DomainEvents.OfType<ExceptionOpenedEvent>().Single();

        await new PublishExceptionOpened(_outbox).Handle(opened, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ExceptionOpenedV1>(e =>
            e.EventType == "ExceptionOpened"
            && e.ExceptionNumber == "EX-0000005"
            && e.ShipmentNumber == "SH-0000012"
            && e.TransportOrderNumber == "TO-0000007"
            && e.ShipperId == "SHP-0001"
            && e.CarrierId == "CAR-0001"
            && e.ExceptionType == "DeliveryFailed"
            && e.Cause == new ExceptionCauseV1("TrackingEvent", "evt-ndl")
            && !e.AfterDelivered
            && e.CorrelationId == "corr-cause"
            && e.OccurredAt == Samples.Now.AddDays(1)));
    }

    [Fact]
    public void ExceptionOpened_ForAProofOfDelivery_NamesItsDocumentAsTheCause_AndIsTracedOnTheProofOfDeliveryFlow()
    {
        var opened = ExceptionOpenedV1.From(OpenExceptions.Reservations().DomainEvents.OfType<ExceptionOpenedEvent>().Single());

        opened.ExceptionType.Should().Be("Reservations");
        opened.Cause.Should().Be(new ExceptionCauseV1("ProofOfDelivery", "doc-1"));
        opened.ApplicationProperties["flow"].Should().Be("carrier-proof-of-delivery");
    }

    [Fact]
    public void ExceptionOpened_TracksItsKeysOnTheCausesTrail_AndFiltersOnWhoCarries()
    {
        var opened = ExceptionOpenedV1.From(OpenExceptions.DeliveryFailed().DomainEvents.OfType<ExceptionOpenedEvent>().Single());

        opened.Topic.Should().Be("shipment-events");
        opened.EventVersion.Should().Be(1);
        opened.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "ExceptionOpened",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-cause",
            ["flow"] = "carrier-tracking",
            ["transportOrderNumber"] = "TO-0000007",
            ["shipmentNumber"] = "SH-0000012",
            ["partyId"] = "CAR-0001",
            ["sourceMessageId"] = "evt-ndl",
            ["assignedTo"] = "Carrier",
        });
    }

    [Fact]
    public void ExceptionOpened_ForOwnFleet_HasNoPartyId_AndFiltersOnOwnFleet()
    {
        var opened = ExceptionOpenedV1.From(OpenExceptions.DeliveryFailed(CarriedBy.OwnFleet).DomainEvents.OfType<ExceptionOpenedEvent>().Single());

        opened.CarrierId.Should().BeNull();
        opened.ApplicationProperties.Should().NotContainKey("partyId").And.Contain("assignedTo", "OwnFleet");
    }

    [Fact]
    public async Task Handle_ExceptionResolved_PublishesExceptionResolved_UnderTheResolutionsOwnCorrelationId()
    {
        var exception = OpenExceptions.DeliveryFailed();
        exception.Resolve(ExceptionOutcome.ReturnedToShipper, "Goods back to the Shipper", new CorrelationId("corr-resolve"), Samples.Now.AddDays(2));
        var resolved = exception.DomainEvents.OfType<ExceptionResolvedEvent>().Single();

        await new PublishExceptionResolved(_outbox).Handle(resolved, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ExceptionResolvedV1>(e =>
            e.EventType == "ExceptionResolved"
            && e.ExceptionNumber == "EX-0000005"
            && e.ShipmentNumber == "SH-0000012"
            && e.ExceptionType == "DeliveryFailed"
            && e.Outcome == "ReturnedToShipper"
            && e.Remark == "Goods back to the Shipper"
            && e.CorrelationId == "corr-resolve"
            && e.OccurredAt == Samples.Now.AddDays(2)));
    }

    [Fact]
    public void ExceptionResolved_TracksItsKeysOnItsOwnTrail()
    {
        var exception = OpenExceptions.DeliveryFailed();
        exception.Resolve(ExceptionOutcome.NoActionNeeded, "ok", new CorrelationId("corr-resolve"), Samples.Now.AddDays(2));

        var resolved = ExceptionResolvedV1.From(exception.DomainEvents.OfType<ExceptionResolvedEvent>().Single());

        resolved.Topic.Should().Be("shipment-events");
        resolved.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "ExceptionResolved",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-resolve",
            ["flow"] = "exception-handling",
            ["transportOrderNumber"] = "TO-0000007",
            ["shipmentNumber"] = "SH-0000012",
        });
    }
}
