using System.Text.Json;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Trips.TakeShipmentOff;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class PublishShipmentTakenOffTripTests
{
    private static readonly JsonSerializerOptions IntegrationEventJson = new(JsonSerializerDefaults.Web);

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    public static TheoryData<string> SampleMessages => new(CanonicalSchemas.ShipmentTakenOffTripSamples());

    [Theory]
    [MemberData(nameof(SampleMessages))]
    public void Schema_SampleMessage_Validates(string sample)
    {
        var results = CanonicalSchemas.ValidateShipmentTakenOffTrip(CanonicalSchemas.ReadSample(sample));

        results.IsValid.Should().BeTrue(CanonicalSchemas.Errors(results));
    }

    [Theory]
    [InlineData("shipmentNumber")]
    [InlineData("transportOrderNumber")]
    [InlineData("tripNumber")]
    [InlineData("version")]
    [InlineData("reason")]
    [InlineData("correlationId")]
    public void Schema_MessageWithoutARequiredProperty_DoesNotValidate(string property)
    {
        var message = CanonicalSchemas.ReadSample(CanonicalSchemas.ShipmentTakenOffTripSamples().First()).AsObject();
        message.Remove(property);

        CanonicalSchemas.ValidateShipmentTakenOffTrip(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Schema_MessageWithAnEmptyReason_DoesNotValidate()
    {
        var message = CanonicalSchemas.ReadSample(CanonicalSchemas.ShipmentTakenOffTripSamples().First()).AsObject();
        message["reason"] = "";

        CanonicalSchemas.ValidateShipmentTakenOffTrip(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShipmentTakenOffTrip_AddsExactlyOneShipmentTakenOffTripToTheOutbox()
    {
        await new PublishShipmentTakenOffTrip(_outbox).Handle(TakenOff(2), CancellationToken.None);

        _outbox.Received(1).Add(Arg.Any<IntegrationEvent>());
        _outbox.Received(1).Add(Arg.Any<ShipmentTakenOffTripV1>());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(null)]
    public void From_ShipmentTakenOffTrip_KeepsToTheSchema_WithTheNumbersVersionReasonAndCorrelationId(int? version)
    {
        var message = ShipmentTakenOffTripV1.From(TakenOff(version));

        var body = JsonSerializer.SerializeToNode(message, message.GetType(), IntegrationEventJson)!;
        var results = CanonicalSchemas.ValidateShipmentTakenOffTrip(body);
        results.IsValid.Should().BeTrue(CanonicalSchemas.Errors(results) + body.ToJsonString());
        message.ShipmentNumber.Should().Be("SH-0000001");
        message.TransportOrderNumber.Should().Be("TO-0000001");
        message.TripNumber.Should().Be("TR-0000007");
        message.Version.Should().Be(version);
        message.Reason.Should().Be("Consignee closed");
        message.CorrelationId.Should().Be("corr-take-off");
    }

    [Fact]
    public void ShipmentTakenOffTrip_Tracks_TheNumbers_OnTheShipmentEventsTopic()
    {
        var message = ShipmentTakenOffTripV1.From(TakenOff(2));

        message.Topic.Should().Be("shipment-events");
        message.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "ShipmentTakenOffTrip",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-take-off",
            ["flow"] = "trip-dispatch",
            ["transportOrderNumber"] = "TO-0000001",
            ["shipmentNumber"] = "SH-0000001",
            ["tripNumber"] = "TR-0000007",
        });
    }

    private static ShipmentTakenOffTripEvent TakenOff(int? version)
    {
        var fixture = new TripsFixture();
        var shipment = fixture.ReleasedShipment(TripsFixture.Tuesday);
        var trip = Trip.CreateDraft(TripNumber.FromSequence(7), TripsFixture.Tuesday, new VehicleId("VEH-0002"), new DriverId("DRV-0001"), new IdempotencyKey("k"));
        return new ShipmentTakenOffTripEvent(trip, shipment, version, "Consignee closed", new CorrelationId("corr-take-off"), new DateTimeOffset(2026, 10, 13, 9, 2, 0, TimeSpan.Zero));
    }
}
