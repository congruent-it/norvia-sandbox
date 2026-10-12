using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Trips.Complete;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class PublishTripCompletedTests
{
    private static readonly JsonSerializerOptions IntegrationEventJson = new(JsonSerializerDefaults.Web);

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    public static TheoryData<string> SampleMessages => new(CanonicalSchemas.TripCompletedSamples());

    [Theory]
    [MemberData(nameof(SampleMessages))]
    public void Schema_SampleMessage_Validates(string sample)
    {
        var results = CanonicalSchemas.ValidateTripCompleted(CanonicalSchemas.ReadSample(sample));

        results.IsValid.Should().BeTrue(CanonicalSchemas.Errors(results));
    }

    [Theory]
    [InlineData("driverId")]
    [InlineData("endedAt")]
    [InlineData("version")]
    [InlineData("tripNumber")]
    [InlineData("correlationId")]
    [InlineData("deliveredShipments")]
    [InlineData("notDeliveredShipments")]
    [InlineData("notCollectedShipments")]
    public void Schema_MessageWithoutARequiredProperty_DoesNotValidate(string property)
    {
        var message = CanonicalSchemas.ReadSample(CanonicalSchemas.TripCompletedSamples().First()).AsObject();
        message.Remove(property);

        CanonicalSchemas.ValidateTripCompleted(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Schema_AShipmentNumberThatIsNoShipmentNumber_DoesNotValidate()
    {
        var message = CanonicalSchemas.ReadSample(CanonicalSchemas.TripCompletedSamples().First()).AsObject();
        message["notDeliveredShipments"] = new JsonArray("not-a-shipment");

        CanonicalSchemas.ValidateTripCompleted(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TripCompleted_AddsExactlyOneTripCompletedToTheOutbox()
    {
        await new PublishTripCompleted(_outbox).Handle(Completed(), CancellationToken.None);

        _outbox.Received(1).Add(Arg.Any<IntegrationEvent>());
        _outbox.Received(1).Add(Arg.Any<TripCompletedV1>());
    }

    [Fact]
    public void From_CompletedTrip_KeepsToTheSchema_WithTheThreeGroupsOfShipments()
    {
        var message = TripCompletedV1.From(CompletedAtVersion1());

        var body = JsonSerializer.SerializeToNode(message, message.GetType(), IntegrationEventJson)!;
        var results = CanonicalSchemas.ValidateTripCompleted(body);
        results.IsValid.Should().BeTrue(CanonicalSchemas.Errors(results) + body.ToJsonString());
        message.TripNumber.Should().Be("TR-0000007");
        message.Version.Should().Be(1);
        message.DriverId.Should().Be("DRV-0001");
        message.EndedAt.Should().Be(new DateTimeOffset(2026, 10, 14, 16, 0, 0, TimeSpan.Zero));
        message.CorrelationId.Should().Be("corr-end");
        message.DeliveredShipments.Should().Equal("SH-0000001");
        message.NotDeliveredShipments.Should().Equal("SH-0000002");
        message.NotCollectedShipments.Should().Equal("SH-0000003");
    }

    [Fact]
    public void TripCompleted_Tracks_TheTripNumber_OnTheTripEventsTopic()
    {
        var message = TripCompletedV1.From(Completed());

        message.Topic.Should().Be("trip-events");
        message.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "TripCompleted",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-end",
            ["flow"] = "trip-execution",
            ["tripNumber"] = "TR-0000007",
        });
    }

    private static TripCompletedEvent Completed() =>
        CompletedBy(Trip.CreateDraft(TripNumber.FromSequence(7), new DateOnly(2026, 10, 14), new VehicleId("VEH-0002"), new DriverId("DRV-0001"), new IdempotencyKey("k")));

    private static TripCompletedEvent CompletedAtVersion1()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(7), new DateOnly(2026, 10, 14), new VehicleId("VEH-0002"), new DriverId("DRV-0001"), new IdempotencyKey("k"));
        typeof(Trip).GetProperty(nameof(Trip.Version))!.SetValue(trip, 1);
        return CompletedBy(trip);
    }

    private static TripCompletedEvent CompletedBy(Trip trip) =>
        new TripCompletedEvent(
            trip,
            trip.DriverId!,
            new CorrelationId("corr-end"),
            new DateTimeOffset(2026, 10, 14, 16, 0, 0, TimeSpan.Zero),
            new TripShipmentGroups([ShipmentNumber.FromSequence(1)], [ShipmentNumber.FromSequence(2)], [ShipmentNumber.FromSequence(3)]));
}
