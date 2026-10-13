using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.UnitTests.Reports;

/// <summary>When the Driver can end a Trip (P12): it has started, every Stop has an outcome, and no End Trip is waiting or recorded.</summary>
public sealed class TripEndRulesTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 13, 8, 5, 12, TimeSpan.FromHours(2));
    private static readonly TripNumber Trip = new("TR-0000012");
    private static readonly StopAddress Address = new("Waalhaven 1", "3087 BM", "Rotterdam", "NL");

    private readonly List<DriverReportRow> _rows = [];

    private readonly TripDetails _details = new(
        new DateOnly(2026, 10, 13), new TimeOnly(9, 0), TimeSpan.FromHours(4), new TripVehicle("VEH-1", "NV-1", "RigidTruck"), "Joost", "corr",
        [
            new TripStop(1, StopKind.Collection, "SH-0000001", "TO-0000001", "SHP-001", Address, null, [], null),
            new TripStop(2, StopKind.Delivery, "SH-0000001", "TO-0000001", "SHP-001", Address, null, [], null),
        ]);

    private DriverReportRow Row(ReportKind kind, ReportState state = ReportState.Recorded, StopKind? stop = null, string? shipment = null) =>
        Add(new(new ReportId(Guid.NewGuid()), kind, Trip, shipment, stop, null, state, null, null, At.AddMinutes(_rows.Count), null));

    private DriverReportRow Add(DriverReportRow row)
    {
        _rows.Add(row);
        return row;
    }

    private void StartAndCompleteEveryStop(ReportState delivered = ReportState.Recorded)
    {
        Row(ReportKind.StartTrip);
        Row(ReportKind.Collected, ReportState.Recorded, StopKind.Collection, "SH-0000001");
        Row(ReportKind.DeliveryFailed, delivered, StopKind.Delivery, "SH-0000001");
    }

    [Fact]
    public void BeforeTheStartIsRecorded_TheTripCannotBeEnded()
    {
        Row(ReportKind.StartTrip, ReportState.Sending);

        TripEndRules.Decide(_details, _rows).Should().Be(new TripEndDecision.NotStarted());
    }

    [Fact]
    public void WhileAStopIsOpen_TheTripCannotBeEnded()
    {
        Row(ReportKind.StartTrip);
        Row(ReportKind.Collected, ReportState.Recorded, StopKind.Collection, "SH-0000001");

        TripEndRules.Decide(_details, _rows).Should().Be(new TripEndDecision.StopsOpen());
    }

    [Fact]
    public void AStopWhoseOutcomeWasRefused_IsOpenAgain()
    {
        StartAndCompleteEveryStop(ReportState.Refused);

        TripEndRules.Decide(_details, _rows).Should().Be(new TripEndDecision.StopsOpen());
    }

    [Theory]
    [InlineData(ReportState.Sending)]
    [InlineData(ReportState.Recorded)]
    public void WhenEveryStopHasAnOutcomeTheTMSHasOrHasntAnsweredYet_TheTripCanBeEnded(ReportState delivered)
    {
        StartAndCompleteEveryStop(delivered);

        TripEndRules.Decide(_details, _rows).Should().Be(new TripEndDecision.Accept());
    }

    [Fact]
    public void ATripWhoseShipmentsWereAllTakenOff_CanBeEndedOnceStarted()
    {
        Row(ReportKind.StartTrip);

        TripEndRules.Decide(_details with { Stops = [] }, _rows).Should().Be(new TripEndDecision.Accept());
    }

    [Theory]
    [InlineData(ReportState.Sending)]
    [InlineData(ReportState.Recorded)]
    public void WhenAnEndIsSendingOrRecorded_ItIsARepeat_AndNothingIsReportedAgain(ReportState state)
    {
        StartAndCompleteEveryStop();
        var end = Row(ReportKind.EndTrip, state);

        TripEndRules.Decide(_details, _rows).Should().Be(new TripEndDecision.Repeat(end.Id));
    }

    [Theory]
    [InlineData(ReportState.Refused)]
    [InlineData(ReportState.NotRecorded)]
    public void AfterARefusedOrFailedEnd_TheDriverCanEndAgain(ReportState state)
    {
        StartAndCompleteEveryStop();
        Row(ReportKind.EndTrip, state);

        TripEndRules.Decide(_details, _rows).Should().Be(new TripEndDecision.Accept());
    }

    [Fact]
    public void TheTripIsEnded_OnceTheTMSHasRecordedTheEnd()
    {
        StartAndCompleteEveryStop();
        TripProgress.IsEnded(_rows).Should().BeFalse();
        Row(ReportKind.EndTrip, ReportState.Sending);
        TripProgress.IsEnded(_rows).Should().BeFalse();
        Row(ReportKind.EndTrip, ReportState.Recorded);
        TripProgress.IsEnded(_rows).Should().BeTrue();
    }
}
