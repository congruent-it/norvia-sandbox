using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.UnitTests.Reports;

/// <summary>What the Driver app lets a Driver report on a Stop (P12), and the Stops it shows, decided in the Domain.</summary>
public sealed class StopReportRulesTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 13, 8, 5, 12, TimeSpan.FromHours(2));
    private static readonly TripNumber Trip = new("TR-0000012");
    private static readonly StopAddress Address = new("Waalhaven 1", "3087 BM", "Rotterdam", "NL");

    private readonly List<DriverReportRow> _rows = [];

    private static TripStop Stop(int sequence, StopKind kind, string shipment = "SH-0000001", StopAddress? address = null) =>
        new(sequence, kind, shipment, "TO-0000001", "SHP-001", address ?? Address, null, [], null);

    private static TripDetails Details(params TripStop[] stops) =>
        new(new DateOnly(2026, 10, 13), new TimeOnly(9, 0), TimeSpan.FromHours(4), new TripVehicle("VEH-1", "NV-1", "RigidTruck"), "Joost", "corr", stops);

    private DriverReportRow Row(ReportKind kind, StopKind? stop = null, string? shipment = null, ReportState state = ReportState.Recorded, string? remark = null, int minutes = 0) =>
        Add(new(new ReportId(Guid.NewGuid()), kind, Trip, shipment, stop, remark, state, null, null, At.AddMinutes(minutes), null));

    private DriverReportRow Add(DriverReportRow row)
    {
        _rows.Add(row);
        return row;
    }

    private StopStatus Status(TripDetails details, StopRef stop, bool includeTakenOff = false) =>
        TripProgress.Of(details, _rows).Single(status => status.Stop == stop && (includeTakenOff || !status.TakenOff));

    private static readonly StopRef Collect = new("SH-0000001", StopKind.Collection);
    private static readonly StopRef Deliver = new("SH-0000001", StopKind.Delivery);

    private readonly TripDetails _trip = Details(Stop(1, StopKind.Collection), Stop(2, StopKind.Delivery));

    [Fact]
    public void BeforeTheStart_NoStopCanBeCompleted_AndDamageCannotBeReported()
    {
        var status = Status(_trip, Collect);

        status.Block.Should().Be(StopBlock.TripNotStarted);
        status.CanComplete.Should().BeFalse();
        StopReportRules.Decide(status, ReportKind.Collected, null).Should().Be(new StopReportDecision.Unavailable(StopBlock.TripNotStarted));
        StopReportRules.Decide(status, ReportKind.Damaged, "Crushed").Should().Be(new StopReportDecision.Unavailable(StopBlock.TripNotStarted));
    }

    [Fact]
    public void OnceStarted_ACollectionIsOpen_AndADeliveryWaitsForItsCollection()
    {
        Row(ReportKind.StartTrip);

        Status(_trip, Collect).Block.Should().Be(StopBlock.None);
        Status(_trip, Deliver).Block.Should().Be(StopBlock.CollectFirst);
        StopReportRules.Decide(Status(_trip, Collect), ReportKind.Collected, null).Should().Be(new StopReportDecision.Accept());
        StopReportRules.Decide(Status(_trip, Deliver), ReportKind.DeliveryFailed, "Closed").Should().Be(new StopReportDecision.Unavailable(StopBlock.CollectFirst));
    }

    [Theory]
    [InlineData(ReportKind.Collected, ReportState.Recorded, StopBlock.None)]
    [InlineData(ReportKind.Collected, ReportState.Sending, StopBlock.None)]
    [InlineData(ReportKind.Collected, ReportState.Refused, StopBlock.CollectFirst)]
    [InlineData(ReportKind.Collected, ReportState.NotRecorded, StopBlock.CollectFirst)]
    [InlineData(ReportKind.CollectionFailed, ReportState.Recorded, StopBlock.CollectionFailed)]
    public void ADeliveryFollowsWhatBecameOfItsCollection(ReportKind collection, ReportState state, StopBlock expected)
    {
        Row(ReportKind.StartTrip);
        Row(collection, StopKind.Collection, "SH-0000001", state, collection == ReportKind.Collected ? null : "Gone", minutes: 1);

        Status(_trip, Deliver).Block.Should().Be(expected);
    }

    [Fact]
    public void AShipmentWithOnlyItsDeliveryStop_ARedelivery_NeedsNoCollection()
    {
        Row(ReportKind.StartTrip);
        var redelivery = Details(Stop(1, StopKind.Delivery));

        Status(redelivery, Deliver).Block.Should().Be(StopBlock.None);
    }

    [Fact]
    public void ACompletedStop_CanBeRepeatedButNotCompletedAnotherWay()
    {
        Row(ReportKind.StartTrip);
        var collected = Row(ReportKind.Collected, StopKind.Collection, "SH-0000001", ReportState.Sending, minutes: 1);

        var status = Status(_trip, Collect);

        status.Completed.Should().BeTrue();
        StopReportRules.Decide(status, ReportKind.Collected, null).Should().Be(new StopReportDecision.Repeat(collected.Id));
        StopReportRules.Decide(status, ReportKind.CollectionFailed, "Changed my mind").Should().Be(new StopReportDecision.AlreadyCompleted());
    }

    [Fact]
    public void Damage_IsAcceptedOnAnyStopOfAStartedTrip_ButTheSameWordsWhileSendingAreOneReport()
    {
        Row(ReportKind.StartTrip);
        var sending = Row(ReportKind.Damaged, StopKind.Delivery, "SH-0000001", ReportState.Sending, "Crushed carton", 1);

        var status = Status(_trip, Deliver);

        StopReportRules.Decide(status, ReportKind.Damaged, "Crushed carton").Should().Be(new StopReportDecision.Repeat(sending.Id));
        StopReportRules.Decide(status, ReportKind.Damaged, "And a torn wrap").Should().Be(new StopReportDecision.Accept());
        status.Completed.Should().BeFalse("damage gives the Stop no outcome");
    }

    [Fact]
    public void ARemarkIsRequiredForAFailureAndDamage_AndNeverMoreThanTheTmsKeeps()
    {
        Row(ReportKind.StartTrip);
        var status = Status(_trip, Collect);

        var blank = StopReportRules.Decide(status, ReportKind.CollectionFailed, " ").Should().BeOfType<StopReportDecision.Invalid>().Subject;
        var damage = StopReportRules.Decide(status, ReportKind.Damaged, null).Should().BeOfType<StopReportDecision.Invalid>().Subject;
        var long_ = StopReportRules.Decide(status, ReportKind.CollectionFailed, new string('x', StopReportRules.MaximumRemarkLength + 1)).Should().BeOfType<StopReportDecision.Invalid>().Subject;

        blank.Problems.Should().ContainSingle().Which.Field.Should().Be("remark");
        damage.Problems.Should().ContainSingle().Which.Field.Should().Be("remark");
        long_.Problems.Should().ContainSingle().Which.Message.Should().Contain("500");
        StopReportRules.Decide(status, ReportKind.Collected, null).Should().Be(new StopReportDecision.Accept());
    }

    [Fact]
    public void AnActionThatDoesNotFitTheKindOfStop_IsInvalid()
    {
        Row(ReportKind.StartTrip);

        StopReportRules.Decide(Status(_trip, Deliver), ReportKind.Collected, null).Should().BeOfType<StopReportDecision.Invalid>().Which.Problems.Should().Contain(problem => problem.Field == "action");
        StopReportRules.Decide(Status(_trip, Collect), ReportKind.DeliveryFailed, "x").Should().BeOfType<StopReportDecision.Invalid>();
        StopReportRules.Fits(ReportKind.StartTrip, StopKind.Collection).Should().BeFalse();
    }

    [Fact]
    public void AStopWithoutACompleteAddress_IsInvalid_BecauseTheTmsNeedsAPlace()
    {
        Row(ReportKind.StartTrip);
        var details = Details(Stop(1, StopKind.Collection, address: new StopAddress("Waalhaven 1", null, "Rotterdam", "NL")));

        StopReportRules.Decide(Status(details, Collect), ReportKind.Collected, null).Should().BeOfType<StopReportDecision.Invalid>()
            .Which.Problems.Should().ContainSingle().Which.Field.Should().Be("address");
    }

    [Fact]
    public void ACompletedStopWhoseShipmentWasTakenOff_IsKept_InTheOrderOfItsReports_AndTakesNoFurtherReport()
    {
        Row(ReportKind.StartTrip);
        Row(ReportKind.Collected, StopKind.Collection, "SH-0000009", minutes: 2);
        Row(ReportKind.Damaged, StopKind.Collection, "SH-0000008", remark: "x", minutes: 1);

        var statuses = TripProgress.Of(_trip, _rows);

        statuses.Select(status => status.Stop).Should().Equal(Collect, Deliver, new StopRef("SH-0000009", StopKind.Collection));
        var takenOff = statuses[2];
        takenOff.TakenOff.Should().BeTrue();
        takenOff.Details.Should().BeNull();
        takenOff.CanComplete.Should().BeFalse();
        takenOff.CanReportDamage.Should().BeFalse();
    }

    [Fact]
    public void TheStops_AreInThePlannersSequence()
    {
        var details = Details(Stop(2, StopKind.Delivery), Stop(1, StopKind.Collection));

        TripProgress.Of(details, _rows).Select(status => status.Stop).Should().Equal(Collect, Deliver);
    }
    [Fact]
    public void ADeliveredIsReportedOnADeliveryStopOnly_AndNeedsNoRemark()
    {
        StopReportRules.Fits(ReportKind.Delivered, StopKind.Delivery).Should().BeTrue();
        StopReportRules.Fits(ReportKind.Delivered, StopKind.Collection).Should().BeFalse();
        StopReportRules.Fits(ReportKind.ProofOfDelivery, StopKind.Delivery).Should().BeTrue();
        StopReportRules.NeedsRemark(ReportKind.Delivered).Should().BeFalse("only reservations need one, which the Proof of delivery's rules ask for");
    }

    [Fact]
    public void ADeliveredCompletesTheStop_WhileAProofOfDeliveryDoesNot_AndADeliveredAgainIsARepeat()
    {
        Row(ReportKind.StartTrip);
        Row(ReportKind.Collected, StopKind.Collection, "SH-0000001");
        var delivered = Row(ReportKind.Delivered, StopKind.Delivery, "SH-0000001", ReportState.Sending, minutes: 5);
        Row(ReportKind.ProofOfDelivery, StopKind.Delivery, "SH-0000001", ReportState.Sending, minutes: 5);

        var status = Status(_trip, Deliver);

        status.Completed.Should().BeTrue();
        status.Outcome!.Kind.Should().Be(ReportKind.Delivered);
        status.Reports.Select(report => report.Kind).Should().Equal(ReportKind.Delivered, ReportKind.ProofOfDelivery);
        StopReportRules.Decide(status, ReportKind.Delivered, null).Should().Be(new StopReportDecision.Repeat(delivered.Id));
        StopReportRules.Decide(status, ReportKind.DeliveryFailed, "Closed").Should().Be(new StopReportDecision.AlreadyCompleted());
    }

    [Fact]
    public void AProofOfDeliveryTheTmsRefused_LeavesTheStopDelivered()
    {
        Row(ReportKind.StartTrip);
        Row(ReportKind.Collected, StopKind.Collection, "SH-0000001");
        Row(ReportKind.Delivered, StopKind.Delivery, "SH-0000001");
        Row(ReportKind.ProofOfDelivery, StopKind.Delivery, "SH-0000001", ReportState.Refused);

        Status(_trip, Deliver).Completed.Should().BeTrue();
    }
}
