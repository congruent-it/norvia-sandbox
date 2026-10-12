using Ardalis.Result;
using Norvia.DriverApp.Application.Reports.ReportOutcome;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;
using NSubstitute;

namespace Norvia.DriverApp.Application.UnitTests.Reports;

/// <summary>The Driver reports Logic App tells the Driver app what became of a report (P12, ADR 0016).</summary>
public sealed class ReportOutcomeHandlerTests
{
    private readonly ReportsWorld _world = new();
    private readonly DriverReport _sending;

    public ReportOutcomeHandlerTests()
    {
        _sending = DriverReport.StartTrip(new ReportId(Guid.NewGuid()), ReportsWorld.TripToday, ReportsWorld.Joost, 2, _world.Time.GetUtcNow(), "corr-1");
        _world.Reports.Add(_sending);
    }

    private Task<Result<ReportOutcomeResult>> Tell(ReportOutcomeKind outcome, string? code = null, string? message = null, Guid? eventId = null, string trip = "TR-0000012") =>
        new ReportOutcomeHandler(_world.ReportRepository, _world.Time)
            .Handle(new ReportOutcomeCommand(eventId ?? _sending.Id.Value, trip, "corr-1", outcome, code, message), CancellationToken.None).AsTask();

    [Fact]
    public async Task Recorded_MakesTheReportRecorded_AndSaves()
    {
        var result = await Tell(ReportOutcomeKind.Recorded);

        result.Value.Should().Be(ReportOutcomeResult.Applied);
        _sending.State.Should().Be(ReportState.Recorded);
        _sending.OutcomeAt.Should().Be(_world.Time.GetUtcNow());
        await _world.ReportRepository.Received(1).UpdateAsync(_sending, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refused_KeepsTheTmsCodeAndMessage()
    {
        var result = await Tell(ReportOutcomeKind.Refused, "NotTripDay", "Trip TR-0000012 is for another day.");

        result.Value.Should().Be(ReportOutcomeResult.Applied);
        _sending.State.Should().Be(ReportState.Refused);
        _sending.FailureCode.Should().Be("NotTripDay");
        _sending.Message.Should().Be("Trip TR-0000012 is for another day.");
    }

    [Fact]
    public async Task NotRecorded_KeepsTheCause()
    {
        var result = await Tell(ReportOutcomeKind.NotRecorded, "TmsUnavailable", "The TMS answered 503 (4 attempts).");

        result.Value.Should().Be(ReportOutcomeResult.Applied);
        _sending.State.Should().Be(ReportState.NotRecorded);
        _sending.FailureCode.Should().Be("TmsUnavailable");
        _sending.Message.Should().Be("The TMS answered 503 (4 attempts).");
    }

    [Fact]
    public async Task AnOutcomeSaidAgain_IsAcknowledged_AndChangesNothing()
    {
        await Tell(ReportOutcomeKind.Recorded);
        _world.ReportRepository.ClearReceivedCalls();

        var again = await Tell(ReportOutcomeKind.Refused, "NotTripDay", "late");

        again.Value.Should().Be(ReportOutcomeResult.Ignored);
        _sending.State.Should().Be(ReportState.Recorded);
        await _world.ReportRepository.DidNotReceive().UpdateAsync(Arg.Any<DriverReport>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownReport_AndAnotherTripsReport_AreNotFound()
    {
        (await Tell(ReportOutcomeKind.Recorded, eventId: Guid.NewGuid())).Status.Should().Be(ResultStatus.NotFound);
        (await Tell(ReportOutcomeKind.Recorded, trip: "TR-0000013")).Status.Should().Be(ResultStatus.NotFound);

        _sending.State.Should().Be(ReportState.Sending);
    }

    [Theory]
    [InlineData(ReportOutcomeKind.Refused, null, "message", "failureCode")]
    [InlineData(ReportOutcomeKind.Refused, "NotTripDay", null, "message")]
    [InlineData(ReportOutcomeKind.NotRecorded, "TmsUnavailable", " ", "message")]
    public async Task ARefusalWithoutItsCodeOrMessage_AndAFailureWithoutItsCause_AreInvalid(ReportOutcomeKind outcome, string? code, string? message, string field)
    {
        var result = await Tell(outcome, code, message);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Select(error => error.Identifier).Should().Contain(field);
        _sending.State.Should().Be(ReportState.Sending);
    }

    [Fact]
    public async Task AMessageLongerThanTheDriverCanRead_IsShortened()
    {
        await Tell(ReportOutcomeKind.NotRecorded, "TmsUnavailable", new string('x', 5000));

        _sending.Message!.Length.Should().Be(1000);
    }
}
