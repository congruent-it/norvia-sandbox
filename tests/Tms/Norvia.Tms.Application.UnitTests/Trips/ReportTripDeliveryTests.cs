using Ardalis.Result;
using Norvia.Tms.Application.Trips.ReportDelivery;
using Norvia.Tms.Domain.Trips;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Trips;

/// <summary>The Trip dispatch Logic App's report that a Trip version reached the Driver app or didn't (P11).</summary>
public sealed class ReportTripDeliveryTests
{
    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task Delivered_ASendingVersion_IsDelivered_RaisesNothing_AndSaves()
    {
        var trip = await _fixture.DispatchedAsync();
        _fixture.Trips.ClearReceivedCalls();

        var result = await Delivered(trip, 1);

        result.Value.Should().Be(new TripDeliveryReportResult(trip.Number.Value, 1, TripDeliveryState.Delivered));
        trip.Deliveries.Single().State.Should().Be(TripDeliveryState.Delivered);
        trip.DomainEvents.Should().BeEmpty();
        await _fixture.Trips.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotDelivered_ASendingVersion_RecordsTheCause_AndRaisesTripNotDelivered()
    {
        var trip = await _fixture.DispatchedAsync();

        var result = await NotDelivered(trip, 1, "The Driver app answered 503.");

        result.Value.Should().Be(new TripDeliveryReportResult(trip.Number.Value, 1, TripDeliveryState.NotDelivered));
        trip.Deliveries.Single().Cause.Should().Be("The Driver app answered 503.");
        trip.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TripNotDeliveredEvent>();
        await _fixture.Trips.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARepeatOrLateReport_IsIgnored_ChangesNothing_AndSavesNothing()
    {
        var trip = await _fixture.DispatchedAsync();
        await Delivered(trip, 1);
        _fixture.Trips.ClearReceivedCalls();

        var repeat = await Delivered(trip, 1);
        var late = await NotDelivered(trip, 1, "Too late");

        repeat.Value.Should().Be(new TripDeliveryReportResult(trip.Number.Value, 1, TripDeliveryState.Delivered, Ignored: true));
        late.Value.Ignored.Should().BeTrue();
        trip.Deliveries.Single().State.Should().Be(TripDeliveryState.Delivered);
        trip.DomainEvents.Should().BeEmpty();
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownTrip_IsRefusedWithTripNotFound()
    {
        var result = await new ReportTripDeliveredHandler(_fixture.Trips, _fixture.Planning.Time)
            .Handle(new ReportTripDeliveredCommand("TR-0009999", 1, "corr-report"), CancellationToken.None);

        Codes(result).Should().Equal(nameof(TripRefusalCode.TripNotFound));
    }

    [Fact]
    public async Task AVersionTheTripNeverHad_IsRefusedWithTripVersionNotFound_AndSavesNothing()
    {
        var trip = await _fixture.DispatchedAsync();
        _fixture.Trips.ClearReceivedCalls();

        var result = await Delivered(trip, 2);

        Codes(result).Should().Equal(nameof(TripRefusalCode.TripVersionNotFound));
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private async Task<Result<TripDeliveryReportResult>> Delivered(Trip trip, int version) =>
        await new ReportTripDeliveredHandler(_fixture.Trips, _fixture.Planning.Time)
            .Handle(new ReportTripDeliveredCommand(trip.Number.Value, version, "corr-report"), CancellationToken.None);

    private async Task<Result<TripDeliveryReportResult>> NotDelivered(Trip trip, int version, string cause) =>
        await new ReportTripNotDeliveredHandler(_fixture.Trips, _fixture.Planning.Time)
            .Handle(new ReportTripNotDeliveredCommand(trip.Number.Value, version, "corr-report", cause), CancellationToken.None);

    private static IEnumerable<string> Codes(Result<TripDeliveryReportResult> result) => result.ValidationErrors.Select(error => error.ErrorCode);
}
