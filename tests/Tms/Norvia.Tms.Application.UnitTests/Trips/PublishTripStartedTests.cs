using System.Text.Json;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Trips.Start;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class PublishTripStartedTests
{
    private static readonly JsonSerializerOptions IntegrationEventJson = new(JsonSerializerDefaults.Web);

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    public static TheoryData<string> SampleMessages => new(CanonicalSchemas.TripStartedSamples());

    [Theory]
    [MemberData(nameof(SampleMessages))]
    public void Schema_SampleMessage_Validates(string sample)
    {
        var results = CanonicalSchemas.ValidateTripStarted(CanonicalSchemas.ReadSample(sample));

        results.IsValid.Should().BeTrue(CanonicalSchemas.Errors(results));
    }

    [Theory]
    [InlineData("driverId")]
    [InlineData("startedAt")]
    [InlineData("version")]
    [InlineData("tripNumber")]
    [InlineData("correlationId")]
    public void Schema_MessageWithoutARequiredProperty_DoesNotValidate(string property)
    {
        var message = CanonicalSchemas.ReadSample(CanonicalSchemas.TripStartedSamples().First()).AsObject();
        message.Remove(property);

        CanonicalSchemas.ValidateTripStarted(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TripStarted_AddsExactlyOneTripStartedToTheOutbox()
    {
        await new PublishTripStarted(_outbox).Handle(Started(), CancellationToken.None);

        _outbox.Received(1).Add(Arg.Any<IntegrationEvent>());
        _outbox.Received(1).Add(Arg.Any<TripStartedV1>());
    }

    [Fact]
    public async Task From_StartedTrip_KeepsToTheSchema_WithTheTripVersionDriverStartTimeAndCorrelationId()
    {
        var message = TripStartedV1.From(await StartedAsync());

        var body = JsonSerializer.SerializeToNode(message, message.GetType(), IntegrationEventJson)!;
        var results = CanonicalSchemas.ValidateTripStarted(body);
        results.IsValid.Should().BeTrue(CanonicalSchemas.Errors(results) + body.ToJsonString());
        message.TripNumber.Should().MatchRegex("^TR-[0-9]{7}$");
        message.Version.Should().Be(1);
        message.DriverId.Should().Be("DRV-0001");
        message.StartedAt.Should().Be(new DateTimeOffset(2026, 10, 13, 6, 30, 0, TimeSpan.Zero));
        message.CorrelationId.Should().Be("corr-start");
    }

    [Fact]
    public void TripStarted_Tracks_TheTripNumber_OnTheTripEventsTopic()
    {
        var message = TripStartedV1.From(Started());

        message.Topic.Should().Be("trip-events");
        message.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "TripStarted",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-start",
            ["flow"] = "trip-execution",
            ["tripNumber"] = "TR-0000007",
        });
    }

    private static TripStartedEvent Started()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(7), new DateOnly(2026, 10, 14), new VehicleId("VEH-0002"), new DriverId("DRV-0001"), new IdempotencyKey("k"));
        return new TripStartedEvent(trip, trip.DriverId!, new CorrelationId("corr-start"), new DateTimeOffset(2026, 10, 14, 6, 30, 0, TimeSpan.Zero));
    }

    private static async Task<TripStartedEvent> StartedAsync()
    {
        var trip = await new TripsFixture().DispatchedAsync();
        trip.Start(new CorrelationId("corr-start"), new DateTimeOffset(2026, 10, 13, 6, 30, 0, TimeSpan.Zero));
        return trip.DomainEvents.OfType<TripStartedEvent>().Single();
    }
}
