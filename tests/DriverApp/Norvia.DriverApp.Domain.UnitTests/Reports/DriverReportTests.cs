using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.UnitTests.Reports;

/// <summary>A report of a Driver is Sending until the Driver reports Logic App tells the Driver app what became of it (P12, ADR 0016).</summary>
public sealed class DriverReportTests
{
    private static readonly DateTimeOffset Pressed = new(2026, 10, 13, 8, 5, 12, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset Answered = Pressed.AddSeconds(3);

    private static DriverReport Start() =>
        DriverReport.StartTrip(new ReportId(Guid.Parse("0198e7c0-0000-7000-8000-0000000000c1")), new TripNumber("TR-0000012"), new DriverId("DRV-0002"), 2, Pressed, "corr-1");

    [Fact]
    public void StartTrip_IsSending_AndKeepsWhatTheDriverDid()
    {
        var report = Start();

        report.Kind.Should().Be(ReportKind.StartTrip);
        report.State.Should().Be(ReportState.Sending);
        report.TripNumber.Should().Be(new TripNumber("TR-0000012"));
        report.DriverId.Should().Be(new DriverId("DRV-0002"));
        report.VersionHeld.Should().Be(2);
        report.EventTime.Should().Be(Pressed);
        report.CorrelationId.Should().Be("corr-1");
        report.FailureCode.Should().BeNull();
        report.Message.Should().BeNull();
        report.OutcomeAt.Should().BeNull();
    }

    [Fact]
    public void Record_ASendingReport_IsRecorded()
    {
        var report = Start();

        report.Record(Answered).Should().BeTrue();

        report.State.Should().Be(ReportState.Recorded);
        report.OutcomeAt.Should().Be(Answered);
    }

    [Fact]
    public void Refuse_ASendingReport_IsRefused_WithTheCodeAndMessage()
    {
        var report = Start();

        report.Refuse("NotTripDay", "Trip TR-0000012 is for another day.", Answered).Should().BeTrue();

        report.State.Should().Be(ReportState.Refused);
        report.FailureCode.Should().Be("NotTripDay");
        report.Message.Should().Be("Trip TR-0000012 is for another day.");
        report.OutcomeAt.Should().Be(Answered);
    }

    [Fact]
    public void GiveUp_ASendingReport_IsNotRecorded_WithTheCause()
    {
        var report = Start();

        report.GiveUp("TmsUnavailable", "The TMS answered 503 (4 attempts).", Answered).Should().BeTrue();

        report.State.Should().Be(ReportState.NotRecorded);
        report.FailureCode.Should().Be("TmsUnavailable");
        report.Message.Should().Be("The TMS answered 503 (4 attempts).");
    }

    [Fact]
    public void AnOutcome_OfAReportThatHasOne_ChangesNothing()
    {
        var report = Start();
        report.Record(Answered);

        report.Record(Answered.AddMinutes(1)).Should().BeFalse();
        report.Refuse("NotTripDay", "late", Answered.AddMinutes(1)).Should().BeFalse();
        report.GiveUp(null, "late", Answered.AddMinutes(1)).Should().BeFalse();

        report.State.Should().Be(ReportState.Recorded);
        report.OutcomeAt.Should().Be(Answered);
        report.FailureCode.Should().BeNull();
    }

    [Theory]
    [InlineData(ReportState.Sending, true)]
    [InlineData(ReportState.Recorded, true)]
    [InlineData(ReportState.Refused, false)]
    [InlineData(ReportState.NotRecorded, false)]
    public void IsActiveStart_IsASendingOrRecordedStart(ReportState state, bool expected)
    {
        var report = Start();
        switch (state)
        {
            case ReportState.Recorded: report.Record(Answered); break;
            case ReportState.Refused: report.Refuse("NotTripDay", "no", Answered); break;
            case ReportState.NotRecorded: report.GiveUp(null, "no", Answered); break;
        }

        report.IsActiveStart.Should().Be(expected);
    }

    [Fact]
    public void StartTrip_WithoutACorrelationIdOrAVersion_IsABug()
    {
        var id = new ReportId(Guid.NewGuid());
        var trip = new TripNumber("TR-0000012");
        var driver = new DriverId("DRV-0002");

        Action noCorrelationId = () => DriverReport.StartTrip(id, trip, driver, 1, Pressed, " ");
        Action noVersion = () => DriverReport.StartTrip(id, trip, driver, 0, Pressed, "c");
        Action noDriver = () => DriverReport.StartTrip(id, trip, new DriverId(""), 1, Pressed, "c");

        noCorrelationId.Should().Throw<ArgumentException>();
        noVersion.Should().Throw<ArgumentOutOfRangeException>();
        noDriver.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Refuse_WithoutACodeOrAMessage_IsABug()
    {
        var report = Start();

        report.Invoking(r => r.Refuse(" ", "message", Answered)).Should().Throw<ArgumentException>();
        report.Invoking(r => r.Refuse("NotTripDay", "", Answered)).Should().Throw<ArgumentException>();
        report.Invoking(r => r.GiveUp("TmsUnavailable", " ", Answered)).Should().Throw<ArgumentException>();
    }
}
