using Ardalis.SharedKernel;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.ConsigneeNotifications;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Application.UnitTests.Trips;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.ConsigneeNotifications;

public sealed class RequestConsigneeNotificationTests
{
    private static readonly DateTimeOffset Earliest = Samples.Now.AddDays(4);
    private static readonly DateTimeOffset EventTime = new(2026, 9, 25, 9, 40, 0, TimeSpan.FromHours(2));

    private static readonly System.Text.Json.JsonSerializerOptions OutboxJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly RequestConsigneeNotification _handler;

    public RequestConsigneeNotificationTests() => _handler = new(_outbox, _orders, Microsoft.Extensions.Logging.Abstractions.NullLogger<RequestConsigneeNotification>.Instance);

    [Fact]
    public async Task Handle_CollectedAndTheOrderHasAnEmail_RequestsANotificationOnTheTrailOfTheTrackingEvent()
    {
        var (collected, order) = Collected();
        GivenOrder(order);

        await _handler.Handle(collected, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ConsigneeNotificationRequestedV1>(e =>
            e.Reason == ConsigneeNotificationReason.Collected
            && e.ShipmentNumber == "SH-0000012"
            && e.TransportOrderNumber == "TO-0000007"
            && e.CorrelationId == "corr-tracking"
            && e.OccurredAt == Samples.Now.AddDays(2)));
    }

    [Fact]
    public async Task Handle_DeliveryWindowAgreedAndTheOrderHasAnEmail_RequestsANotificationOnTheAgreementsTrail()
    {
        var (agreed, order) = Agreed();
        GivenOrder(order);

        await _handler.Handle(agreed, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ConsigneeNotificationRequestedV1>(e =>
            e.Reason == ConsigneeNotificationReason.DeliveryWindowAgreed
            && e.TransportOrderNumber == "TO-0000007"
            && e.CorrelationId == "corr-agree"
            && e.OccurredAt == Samples.Now.AddDays(3)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not an email")]
    public async Task Handle_TheOrderHasNoUsableEmail_SendsNothing_AndIsNoError(string? email)
    {
        var (collected, order) = Collected(email);
        GivenOrder(order);

        await _handler.Handle(collected, CancellationToken.None);

        _outbox.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Handle_TheOrderIsNotFound_SendsNothing_AndIsNoError()
    {
        var (agreed, _) = Agreed();
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns((TransportOrder?)null);

        await _handler.Handle(agreed, CancellationToken.None);

        _outbox.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Handle_TheOrderCantBeRead_SendsNothing_AndNeverFailsTheCollectedOrTheAgreement()
    {
        var (collected, _) = Collected();
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns<TransportOrder?>(_ => throw new InvalidOperationException("database down"));

        var handle = async () => await _handler.Handle(collected, CancellationToken.None);

        await handle.Should().NotThrowAsync();
        _outbox.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Handle_Canceled_StillCancels()
    {
        var (collected, _) = Collected();
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns<TransportOrder?>(_ => throw new OperationCanceledException());

        var handle = async () => await _handler.Handle(collected, new CancellationToken(true));

        await handle.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Handle_TheOrderHasNoTrackingCode_SendsNothing()
    {
        var (collected, order) = Collected();
        order.GetType().GetProperty(nameof(TransportOrder.TrackingCode))!.SetValue(order, null);
        GivenOrder(order);

        await _handler.Handle(collected, CancellationToken.None);

        _outbox.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public void ConsigneeNotificationRequested_IsOnTheShipmentEventsTopic_AndCarriesNoPersonalDataAndNoTrackingCode()
    {
        var (agreed, _) = Agreed();
        var requested = ConsigneeNotificationRequestedV1.From(agreed);

        requested.Topic.Should().Be("shipment-events");
        requested.EventType.Should().Be("ConsigneeNotificationRequested");
        requested.EventVersion.Should().Be(1);
        requested.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "ConsigneeNotificationRequested",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-agree",
            ["flow"] = "consignee-notification",
            ["transportOrderNumber"] = "TO-0000007",
            ["shipmentNumber"] = "SH-0000012",
        });
        System.Text.Json.JsonSerializer.Serialize(requested, System.Text.Json.JsonSerializerOptions.Web)
            .Should().NotContainEquivalentOf("email").And.NotContainEquivalentOf("trackingCode").And.NotContain("zwaan");
    }

    public static TheoryData<string> SampleMessages => new(CanonicalSchemas.ConsigneeNotificationRequestedSamples());

    [Theory]
    [MemberData(nameof(SampleMessages))]
    public void Sample_IsAValidConsigneeNotificationRequested(string sample)
    {
        var results = CanonicalSchemas.ValidateConsigneeNotificationRequested(CanonicalSchemas.ReadSample(sample));

        results.IsValid.Should().BeTrue(CanonicalSchemas.Errors(results));
    }

    [Fact]
    public void ConsigneeNotificationRequested_AsTheOutboxSerializesIt_FollowsTheSchema_ForBothReasons()
    {
        var (collected, _) = Collected();
        var (agreed, _) = Agreed();

        foreach (var requested in new[] { ConsigneeNotificationRequestedV1.From(collected), ConsigneeNotificationRequestedV1.From(agreed) })
        {
            var body = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(requested, requested.GetType(), OutboxJson))!;
            var results = CanonicalSchemas.ValidateConsigneeNotificationRequested(body);

            results.IsValid.Should().BeTrue(CanonicalSchemas.Errors(results));
        }
    }

    private void GivenOrder(TransportOrder order) =>
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(order);

    private static TransportOrder OrderWith(string? email)
    {
        var canonical = Samples.CanonicalTransportOrder();
        canonical = canonical with
        {
            Delivery = canonical.Delivery! with { Consignee = new CanonicalConsignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142", email) },
        };
        return TransportOrder.Accept(
            new Norvia.Tms.Domain.Shippers.ShipperId("SHP-0001"), canonical.ToDetails(), TransportOrderNumber.FromSequence(7), IntakeChannel.ShipperApi, new CorrelationId("corr-1"), Samples.Now);
    }

    private static (ShipmentCollectedEvent Event, TransportOrder Order) Collected(string? email = "inkoop@dezwaan.example")
    {
        var order = OrderWith(email);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12);
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(1), Norvia.Tms.Domain.CarrierBookings.CarrierBookingNumber.FromSequence(1));
        shipment.Book();
        shipment.ClearDomainEvents();
        shipment.Record(
            new TrackingReport(TrackingSource.Carrier(new CarrierId("CAR-0001")), "evt-1", "VDT-1", null, TrackingEventType.Collected, EventTime, new TrackingLocation("Rotterdam", "3011 AA", "NL"), null, Remark: null),
            new CorrelationId("corr-tracking"),
            Samples.Now.AddDays(2));
        return ((ShipmentCollectedEvent)shipment.DomainEvents.Single(), order);
    }

    private static (ShipmentDeliveryWindowAgreedEvent Event, TransportOrder Order) Agreed()
    {
        var order = OrderWith("inkoop@dezwaan.example");
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12);
        var window = new AgreedDeliveryWindow(Earliest, Earliest.AddHours(4), ExceptionNumber.FromSequence(5), new CorrelationId("corr-agree"), Samples.Now.AddDays(3), new IdempotencyKey("key-agree"));
        return (new ShipmentDeliveryWindowAgreedEvent(shipment, window), order);
    }
}
