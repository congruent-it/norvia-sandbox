using Norvia.Tms.Application.Exceptions.ListOpen;
using Norvia.Tms.Application.ProofsOfDelivery.ListMissing;
using Norvia.Tms.Application.Shipments.ListAwaitingAnswer;
using Norvia.Tms.Application.Shipments.ListToPutOnTrips;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Application.TransportOrders.ToPlan;
using Norvia.Tms.Web.Planner;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Planner;

public sealed class PlannerHomeTests : TmsWebContext
{
    [Fact]
    public void Render_BeforeTheCountsAreRead_ShowsLoading()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<TransportOrderToPlanSummary>>();
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>())
            .Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = Render<PlannerHome>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the work to act on");
    }

    [Fact]
    public void Render_WithWorkToDo_ShowsEachCountWithALinkToItsList()
    {
        GivenLists(waiting: 3, toRelease: 2, exceptions: 1);

        var page = Render<PlannerHome>();

        var item = page.WaitForElement("li:has(a[href='/transport-orders'])");
        item.TextContent.Should().Contain("3").And.Contain("Transport Orders to plan");
        page.Find("li:has(a[href='/shipments/to-release'])").TextContent.Should().Contain("2");
        page.Find("li:has(a[href='/exceptions'])").TextContent.Should().Contain("1");
    }

    [Fact]
    public void Render_WithNothingToDoForAnItem_SaysSoInsteadOfACount()
    {
        GivenLists(waiting: 3, toRelease: 0, exceptions: 0);

        var page = Render<PlannerHome>();

        var item = page.WaitForElement("li:has(a[href='/shipments/to-release'])");
        item.TextContent.Should().Contain("No Shipments are waiting to be released.");
        item.QuerySelector("strong").Should().BeNull();
    }

    [Fact]
    public void Render_WithWorkToDo_ShowsTheActionToTakeOnTheCard()
    {
        GivenLists(waiting: 3, toRelease: 2, exceptions: 0);

        var page = Render<PlannerHome>();

        page.WaitForElement("li:has(a[href='/shipments/to-release'])").TextContent.Should().Contain("Release them");
        page.Find("li:has(a[href='/transport-orders'])").TextContent.Should().Contain("Plan them");
    }

    [Fact]
    public void Render_WithNothingToDoForAnItem_ShowsNoActionOnTheCard()
    {
        GivenLists(waiting: 3, toRelease: 0, exceptions: 0);

        var page = Render<PlannerHome>();

        page.WaitForElement("li:has(a[href='/shipments/to-release'])").TextContent.Should().NotContain("Release them");
    }

    private void GivenLists(int waiting, int toRelease, int exceptions)
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns(new TransportOrderToPlanSummary[waiting]);
        Queries.ListShipmentsToReleaseAsync(Arg.Any<CancellationToken>()).Returns(new ShipmentToReleaseSummary[toRelease]);
        Queries.ListCarrierBookingsAwaitingAnswerAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CarrierBookingAwaitingAnswerSummary>());
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(new OpenExceptionSummary[exceptions]);
        Queries.ListDeliveredShipmentsWithoutProofOfDeliveryAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<DeliveredShipmentWithoutProofOfDeliverySummary>());
        Queries.ListShipmentsToPutOnTripsAsync(null, Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipmentToPutOnTripSummary>());
    }
}
