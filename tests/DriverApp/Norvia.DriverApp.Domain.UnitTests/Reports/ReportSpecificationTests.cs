using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.UnitTests.Reports;

public sealed class ReportSpecificationTests
{
    private static readonly DateTimeOffset Pressed = new(2026, 10, 13, 8, 5, 12, TimeSpan.FromHours(2));

    private static DriverReport Start(string trip, int minutes, Action<DriverReport>? then = null)
    {
        var report = DriverReport.StartTrip(new ReportId(Guid.NewGuid()), new TripNumber(trip), new DriverId("DRV-0002"), 1, Pressed.AddMinutes(minutes), "corr");
        then?.Invoke(report);
        return report;
    }

    [Fact]
    public void ReportsOfTrips_ListsTheReportsOfTheTripsNamedOnly_OldestFirst_WithWhatTheDriverSees()
    {
        var late = Start("TR-0000001", 10, r => r.Refuse("NotTripDay", "Wrong day.", Pressed.AddMinutes(11)));
        var early = Start("TR-0000001", 1);
        var other = Start("TR-0000002", 5);
        var elsewhere = Start("TR-0000003", 6);

        var rows = new ReportsOfTripsSpecification([new TripNumber("TR-0000001"), new TripNumber("TR-0000002")])
            .Evaluate([late, early, other, elsewhere]).ToList();

        rows.Select(row => row.Id).Should().Equal(early.Id, other.Id, late.Id);
        rows[2].Should().Be(new DriverReportRow(
            late.Id, ReportKind.StartTrip, new TripNumber("TR-0000001"), null, null, null, ReportState.Refused, "NotTripDay", "Wrong day.", Pressed.AddMinutes(10), Pressed.AddMinutes(11)));
    }

    private static DriverReport Stop(string trip, string shipment, StopKind stop, ReportKind kind, Action<DriverReport>? then = null)
    {
        var report = DriverReport.ReportStop(new ReportId(Guid.NewGuid()), kind, new StopRef(shipment, stop), new ReportOrigin(new TripNumber(trip), new DriverId("DRV-0002"), 1, Pressed, "corr"), "remark");
        then?.Invoke(report);
        return report;
    }

    [Fact]
    public void ReportsOfTrips_ReadWhatAStopReportNamed()
    {
        var failed = Stop("TR-0000001", "SH-0000007", StopKind.Delivery, ReportKind.DeliveryFailed);

        var row = new ReportsOfTripsSpecification([new TripNumber("TR-0000001")]).Evaluate([failed]).Single();

        row.ShipmentNumber.Should().Be("SH-0000007");
        row.Stop.Should().Be(StopKind.Delivery);
        row.Remark.Should().Be("remark");
        row.Kind.Should().Be(ReportKind.DeliveryFailed);
    }

    [Fact]
    public void ActiveStartOfTrip_FindsASendingOrRecordedStartOfThatTrip_AndNothingElse()
    {
        var sending = Start("TR-0000001", 1);
        var recordedElsewhere = Start("TR-0000002", 1, r => r.Record(Pressed));
        var refused = Start("TR-0000003", 1, r => r.Refuse("NotTripDay", "no", Pressed));
        var notRecorded = Start("TR-0000004", 1, r => r.GiveUp(null, "gave up", Pressed));
        DriverReport[] all = [sending, recordedElsewhere, refused, notRecorded];

        new ActiveStartOfTripSpecification(new TripNumber("TR-0000001")).Evaluate(all).Should().Equal(sending);
        new ActiveStartOfTripSpecification(new TripNumber("TR-0000002")).Evaluate(all).Should().Equal(recordedElsewhere);
        new ActiveStartOfTripSpecification(new TripNumber("TR-0000003")).Evaluate(all).Should().BeEmpty();
        new ActiveStartOfTripSpecification(new TripNumber("TR-0000004")).Evaluate(all).Should().BeEmpty();
    }
}
