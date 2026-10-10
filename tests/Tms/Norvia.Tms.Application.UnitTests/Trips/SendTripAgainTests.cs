using Ardalis.Result;
using Norvia.Tms.Application.Trips.SendAgain;
using Norvia.Tms.Domain.Trips;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Trips;

/// <summary>The Planner sends a Not delivered Trip version to the Driver app again (P11).</summary>
public sealed class SendTripAgainTests
{
    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task ANotDeliveredVersion_IsSendingAgain_AndRaisesTripDispatchedWithTheSameVersionAndTheNewCorrelationId()
    {
        var trip = await NotDeliveredAsync();
        _fixture.Trips.ClearReceivedCalls();

        var result = await SendAgain(trip.Number.Value, 1, "corr-again");

        result.Value.Should().Be(new SendTripAgainResult(trip.Number.Value, 1, "corr-again"));
        trip.Deliveries.Single().State.Should().Be(TripDeliveryState.Sending);
        var raised = trip.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TripDispatchedEvent>().Subject;
        raised.Trip.Version.Should().Be(1);
        raised.CorrelationId.Value.Should().Be("corr-again");
        raised.Shipments.Should().ContainSingle();
        raised.Vehicle.Registration.Should().Be("VV-102-B");
        raised.Driver.Id.Value.Should().Be("DRV-0001");
        await _fixture.Trips.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AVersionThatIsStillSending_IsRefusedWithNothingToSendAgain_AndNothingChanges()
    {
        var trip = await _fixture.DispatchedAsync();
        _fixture.Trips.ClearReceivedCalls();

        var result = await SendAgain(trip.Number.Value, 1, "corr-again");

        Codes(result).Should().Equal(nameof(TripRefusalCode.NothingToSendAgain));
        trip.DomainEvents.Should().BeEmpty();
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AVersionTheTripNeverHad_IsRefusedWithTripVersionNotFound()
    {
        var trip = await NotDeliveredAsync();

        Codes(await SendAgain(trip.Number.Value, 2, "corr-again")).Should().Equal(nameof(TripRefusalCode.TripVersionNotFound));
    }

    [Fact]
    public async Task ADraft_IsRefusedWithTripNotDispatched()
    {
        var draft = _fixture.Existing(TripsFixture.Tuesday, null, null);

        Codes(await SendAgain(draft.Number.Value, 1, "corr-again")).Should().Equal(nameof(TripRefusalCode.TripNotDispatched));
    }

    [Fact]
    public async Task AnUnknownTrip_IsRefusedWithTripNotFound()
    {
        Codes(await SendAgain("TR-0009999", 1, "corr-again")).Should().Equal(nameof(TripRefusalCode.TripNotFound));
    }

    [Fact]
    public async Task WhenTheVehicleIsNoLongerActive_IsRefusedWithVehicleOrDriverInactive_AndNothingChanges()
    {
        var trip = await NotDeliveredAsync();
        _fixture.Planning.Vehicles.ListActive().Returns([]);
        _fixture.Trips.ClearReceivedCalls();

        Codes(await SendAgain(trip.Number.Value, 1, "corr-again")).Should().Equal(nameof(TripRefusalCode.VehicleOrDriverInactive));
        trip.Deliveries.Single().State.Should().Be(TripDeliveryState.NotDelivered);
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private async Task<Trip> NotDeliveredAsync()
    {
        var trip = await _fixture.DispatchedAsync();
        trip.ReportNotDelivered(1, "The Driver app answered 503.", new Norvia.Tms.Domain.TransportOrders.CorrelationId("corr-report"), _fixture.Planning.Time.GetUtcNow());
        trip.ClearDomainEvents();
        return trip;
    }

    private async Task<Result<SendTripAgainResult>> SendAgain(string number, int version, string correlationId) =>
        await new SendTripAgainHandler(_fixture.Trips, _fixture.TripShipments, _fixture.Planning.Vehicles, _fixture.Planning.Drivers, _fixture.Planning.Time)
            .Handle(new SendTripAgainCommand(number, version, correlationId), CancellationToken.None);

    private static IEnumerable<string> Codes(Result<SendTripAgainResult> result) => result.ValidationErrors.Select(error => error.ErrorCode);
}
