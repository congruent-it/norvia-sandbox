using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.UnitTests.Reports;

/// <summary>A Driver's Stop report names the Shipment and the Stop, and the three that complete a Stop give it an outcome; Report damage doesn't (P12).</summary>
public sealed class StopReportTests
{
    private static readonly DateTimeOffset Pressed = new(2026, 10, 13, 8, 5, 12, TimeSpan.FromHours(2));

    private static DriverReport Report(ReportKind kind, StopKind stop, string? remark = null) =>
        DriverReport.ReportStop(
            new ReportId(Guid.NewGuid()), kind, new StopRef("SH-0000007", stop), new ReportOrigin(new TripNumber("TR-0000012"), new DriverId("DRV-0002"), 2, Pressed, "corr-1"), remark);

    [Fact]
    public void AStopReport_IsSending_AndKeepsTheShipmentTheStopAndTheRemark()
    {
        var report = Report(ReportKind.CollectionFailed, StopKind.Collection, "Nobody at the dock");

        report.State.Should().Be(ReportState.Sending);
        report.Kind.Should().Be(ReportKind.CollectionFailed);
        report.ShipmentNumber.Should().Be("SH-0000007");
        report.Stop.Should().Be(StopKind.Collection);
        report.Remark.Should().Be("Nobody at the dock");
        report.TripNumber.Should().Be(new TripNumber("TR-0000012"));
        report.DriverId.Should().Be(new DriverId("DRV-0002"));
        report.VersionHeld.Should().Be(2);
        report.EventTime.Should().Be(Pressed);
    }

    [Theory]
    [InlineData(ReportKind.Collected, StopKind.Collection, true)]
    [InlineData(ReportKind.CollectionFailed, StopKind.Collection, true)]
    [InlineData(ReportKind.DeliveryFailed, StopKind.Delivery, true)]
    [InlineData(ReportKind.Damaged, StopKind.Collection, false)]
    [InlineData(ReportKind.Damaged, StopKind.Delivery, false)]
    public void OnlyTheReportsThatCompleteAStop_GiveItAnOutcome(ReportKind kind, StopKind stop, bool givesAnOutcome) =>
        Report(kind, stop, "remark").CompletesStop.Should().Be(givesAnOutcome);

    [Fact]
    public void AStartTrip_GivesNoStopAnOutcome() =>
        DriverReport.StartTrip(new ReportId(Guid.NewGuid()), new TripNumber("TR-0000012"), new DriverId("DRV-0002"), 1, Pressed, "c").CompletesStop.Should().BeFalse();

    [Theory]
    [InlineData(ReportKind.CollectionFailed, StopKind.Collection)]
    [InlineData(ReportKind.DeliveryFailed, StopKind.Delivery)]
    [InlineData(ReportKind.Damaged, StopKind.Delivery)]
    public void AFailureOrDamage_NeedsARemark(ReportKind kind, StopKind stop)
    {
        Action withoutRemark = () => Report(kind, stop, " ");

        withoutRemark.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Collected_NeedsNoRemark_ButMayHaveOne() =>
        Report(ReportKind.Collected, StopKind.Collection, "Two pallets short").Remark.Should().Be("Two pallets short");

    [Theory]
    [InlineData(ReportKind.Collected, StopKind.Delivery)]
    [InlineData(ReportKind.CollectionFailed, StopKind.Delivery)]
    [InlineData(ReportKind.DeliveryFailed, StopKind.Collection)]
    public void AReportOfAnotherKindOfStop_IsABug(ReportKind kind, StopKind stop)
    {
        Action wrongStop = () => Report(kind, stop, "remark");

        wrongStop.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StartTrip_IsNotAStopReport()
    {
        Action start = () => Report(ReportKind.StartTrip, StopKind.Collection);

        start.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AStopReport_WithoutAShipment_IsABug()
    {
        Action noShipment = () => DriverReport.ReportStop(
            new ReportId(Guid.NewGuid()), ReportKind.Collected, new StopRef(" ", StopKind.Collection), new ReportOrigin(new TripNumber("TR-0000012"), new DriverId("DRV-0002"), 2, Pressed, "c"), null);

        noShipment.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ARecordedStopReport_StaysRecorded_AndARefusedOneKeepsTheTmsCode()
    {
        var report = Report(ReportKind.Collected, StopKind.Collection);

        report.Refuse("ShipmentNotOnTrip", "SH-0000007 is not on TR-0000012.", Pressed.AddSeconds(2)).Should().BeTrue();

        report.State.Should().Be(ReportState.Refused);
        report.FailureCode.Should().Be("ShipmentNotOnTrip");
    }
}
