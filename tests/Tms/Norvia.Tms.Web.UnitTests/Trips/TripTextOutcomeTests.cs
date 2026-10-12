using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListInProgress;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.GetByNumber;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.Trips;

namespace Norvia.Tms.Web.UnitTests.Trips;

public sealed class TripTextOutcomeTests
{
    private static readonly TripView Draft = new("TR-0000001", new DateOnly(2026, 10, 13), TripStatus.Draft, null, null, null, null, null, null, StopCount: 6);

    [Fact]
    public void Stops_BeforeTheTripStarts_IsTheCount_AndOnceUnderWayHowManyAreDone()
    {
        TripText.Stops(Draft).Should().Be("6");
        TripText.Stops(Draft with { Status = TripStatus.InProgress, StopsDone = 2 }).Should().Be("2 of 6 done");
    }

    [Fact]
    public void Outcome_IsOpenUntilReported_ThenNamesTheOutcomeWithItsTimeAndRemark()
    {
        var open = Stop(StopOutcomeKind.Open, null, null);
        var failed = Stop(StopOutcomeKind.DeliveryFailed, new DateTimeOffset(2026, 10, 13, 14, 5, 0, TimeSpan.FromHours(2)), "Nobody home");

        TripText.Outcome(open).Should().Be("Open");
        TripText.Outcome(failed).Should().StartWith("Delivery failed · ").And.EndWith(" · Nobody home");
        TripText.Outcome(Stop(StopOutcomeKind.Collected, null, null)).Should().Be("Collected");
        TripText.Outcome(Stop(StopOutcomeKind.CollectionFailed, null, null)).Should().Be("Collection failed");
    }

    [Fact]
    public void CarriedBy_ForAnOwnFleetShipmentInProgress_NamesOwnFleetAndItsTrip()
    {
        var own = new ShipmentInProgressSummary("SH-0000012", new CarriedByView(AssignedTo.OwnFleet, null), ShipmentStatus.Dispatched, null, null, false, false, false, "TR-0000042");

        TrackingText.CarriedBy(own).Should().Be("Own fleet · TR-0000042");
        TrackingText.CarriedBy(own with { TripNumber = null }).Should().Be("Own fleet");
    }

    private static TripStopView Stop(StopOutcomeKind kind, DateTimeOffset? at, string? remark) =>
        new(1, StopKind.Delivery, "SH-0000012", "TO-0000007", "Oudegracht 112", null, null, null, "2 pallet", kind, at, remark);
}
