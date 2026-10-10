using System.Text.Json;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Trips.ReportDelivery;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class PublishTripNotDeliveredTests
{
    private static readonly JsonSerializerOptions IntegrationEventJson = new(JsonSerializerDefaults.Web);

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    public static TheoryData<string> SampleMessages => new(CanonicalSchemas.TripNotDeliveredSamples());

    [Theory]
    [MemberData(nameof(SampleMessages))]
    public void Schema_SampleMessage_Validates(string sample)
    {
        var results = CanonicalSchemas.ValidateTripNotDelivered(CanonicalSchemas.ReadSample(sample));

        results.IsValid.Should().BeTrue(CanonicalSchemas.Errors(results));
    }

    [Theory]
    [InlineData("driverId")]
    [InlineData("cause")]
    [InlineData("version")]
    [InlineData("tripNumber")]
    [InlineData("correlationId")]
    public void Schema_MessageWithoutARequiredProperty_DoesNotValidate(string property)
    {
        var message = CanonicalSchemas.ReadSample(CanonicalSchemas.TripNotDeliveredSamples().First()).AsObject();
        message.Remove(property);

        CanonicalSchemas.ValidateTripNotDelivered(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Schema_MessageWithAnEmptyCause_DoesNotValidate()
    {
        var message = CanonicalSchemas.ReadSample(CanonicalSchemas.TripNotDeliveredSamples().First()).AsObject();
        message["cause"] = "";

        CanonicalSchemas.ValidateTripNotDelivered(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TripNotDelivered_AddsExactlyOneTripNotDeliveredToTheOutbox()
    {
        await new PublishTripNotDelivered(_outbox).Handle(NotDelivered(), CancellationToken.None);

        _outbox.Received(1).Add(Arg.Any<IntegrationEvent>());
        _outbox.Received(1).Add(Arg.Any<TripNotDeliveredV1>());
    }

    [Fact]
    public void From_NotDeliveredTrip_KeepsToTheSchema_WithTheTripVersionDriverCauseAndCorrelationId()
    {
        var message = TripNotDeliveredV1.From(NotDelivered());

        var body = JsonSerializer.SerializeToNode(message, message.GetType(), IntegrationEventJson)!;
        var results = CanonicalSchemas.ValidateTripNotDelivered(body);
        results.IsValid.Should().BeTrue(CanonicalSchemas.Errors(results) + body.ToJsonString());
        message.TripNumber.Should().Be("TR-0000007");
        message.Version.Should().Be(1);
        message.DriverId.Should().Be("DRV-0001");
        message.Cause.Should().Be("The Driver app answered 503.");
        message.CorrelationId.Should().Be("corr-report");
    }

    [Fact]
    public void TripNotDelivered_Tracks_TheTripNumber_OnTheTripEventsTopic()
    {
        var message = TripNotDeliveredV1.From(NotDelivered());

        message.Topic.Should().Be("trip-events");
        message.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "TripNotDelivered",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-report",
            ["flow"] = "trip-dispatch",
            ["tripNumber"] = "TR-0000007",
        });
    }

    private static TripNotDeliveredEvent NotDelivered()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(7), new DateOnly(2026, 10, 14), new Norvia.Tms.Domain.Fleet.VehicleId("VEH-0002"), new Norvia.Tms.Domain.Fleet.DriverId("DRV-0001"), new IdempotencyKey("k"));
        return new TripNotDeliveredEvent(trip, 1, trip.DriverId!, "The Driver app answered 503.", new CorrelationId("corr-report"), new DateTimeOffset(2026, 10, 13, 9, 2, 0, TimeSpan.Zero));
    }
}
