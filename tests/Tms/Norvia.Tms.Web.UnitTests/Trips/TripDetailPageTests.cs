using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.GetByNumber;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Web.Trips;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Trips;

/// <summary>The Planner's Trip detail (P11): the Stops in order, the Vehicle and Driver, the load against the capacity, the rule failures and the warnings.</summary>
public sealed class TripDetailPageTests : TmsWebContext
{
    private static readonly DateOnly Wednesday = new(2026, 10, 14);
    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);

    private static readonly TripView Trip = new(
        "TR-0000001", Wednesday, TripStatus.Draft, "VEH-0001", "VN-101-A", "DRV-0001", "Anouk de Vries", new TimeOnly(7, 30), TimeSpan.FromMinutes(390),
        StopCount: 2, LoadWeightKg: 1_500m, LoadPalletPlaces: 1, PayloadKg: 1_200, VehiclePalletPlaces: 4);

    private static readonly TripStopView Collect = new(
        0, StopKind.Collection, "SH-0000001", "TO-0000001", "Waalhaven Oostzijde 88, 3087 BM Rotterdam", null,
        new DateTimeOffset(2026, 10, 14, 8, 0, 0, Cest), new DateTimeOffset(2026, 10, 14, 12, 0, 0, Cest), "1 pallet");

    private static readonly TripStopView Deliver = Collect with
    {
        Position = 1,
        Kind = StopKind.Delivery,
        Place = "Oudegracht 112, 3511 AW Utrecht",
        Name = "Café De Zwaan",
        WindowEarliest = new DateTimeOffset(2026, 10, 15, 13, 0, 0, Cest),
        WindowLatest = new DateTimeOffset(2026, 10, 15, 17, 0, 0, Cest),
    };

    [Fact]
    public void Render_ShowsTheStopsInOrder_WithPlaceWindowAndGoods()
    {
        Queries.FindTripAsync("TR-0000001", Arg.Any<CancellationToken>())
            .Returns(new TripDetailView(Trip, [Collect, Deliver], [], []));

        var page = Render<TripDetailPage>(parameters => parameters.Add(detail => detail.Number, "TR-0000001"));

        var rows = page.WaitForElements("table[aria-label='Stops'] tbody tr, [aria-label='Stops'] tbody tr");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("Collect").And.Contain("SH-0000001").And.Contain("Waalhaven Oostzijde 88").And.Contain("08:00–12:00").And.Contain("1 pallet");
        rows[1].TextContent.Should().Contain("Deliver").And.Contain("Café De Zwaan, Oudegracht 112").And.Contain("Thu 15 Oct 2026, 13:00–17:00");
    }

    [Fact]
    public void Render_ShowsTheVehicleDriverTimesAndTheLoadAgainstTheCapacity()
    {
        Queries.FindTripAsync("TR-0000001", Arg.Any<CancellationToken>())
            .Returns(new TripDetailView(Trip, [Collect, Deliver], [], []));

        var page = Render<TripDetailPage>(parameters => parameters.Add(detail => detail.Number, "TR-0000001"));

        var summary = page.WaitForElement("dl[aria-label=Trip]").TextContent;
        summary.Should().Contain("Draft").And.Contain("VN-101-A").And.Contain("Anouk de Vries").And.Contain("07:30").And.Contain("6:30")
            .And.Contain("1,500 / 1,200 kg · 1 / 4 pallets");
    }

    [Fact]
    public void Render_ShowsTheRuleFailuresAndTheWarnings()
    {
        Queries.FindTripAsync("TR-0000001", Arg.Any<CancellationToken>()).Returns(new TripDetailView(
            Trip,
            [Collect, Deliver],
            [new TripRuleFailureView("PayloadExceeded", "The Trip's 1,500 kg is more than Vehicle VEH-0001's 1,200 kg payload.")],
            [new TripWarning(TripWarning.DeliveryWindowOutsideTripDay, "Shipment SH-0000001's delivery window doesn't include the Trip's day 2026-10-14.")]));

        var page = Render<TripDetailPage>(parameters => parameters.Add(detail => detail.Number, "TR-0000001"));

        page.WaitForElement("ul[aria-label='Rule failures']").TextContent.Should().Contain("PayloadExceeded").And.Contain("more than Vehicle VEH-0001");
        page.Find("ul[aria-label=Warnings]").TextContent.Should().Contain("delivery window doesn't include the Trip's day");
    }

    [Fact]
    public void Render_ATripWithoutStops_PointsToTheShipmentsToPutOnTrips()
    {
        Queries.FindTripAsync("TR-0000001", Arg.Any<CancellationToken>())
            .Returns(new TripDetailView(Trip with { StopCount = 0 }, [], [], []));

        var page = Render<TripDetailPage>(parameters => parameters.Add(detail => detail.Number, "TR-0000001"));

        page.WaitForElement("a[href='/trips/shipments']").TextContent.Should().Contain("Shipments to put on Trips");
    }

    [Fact]
    public void Render_AnUnknownTrip_SaysThereIsNone()
    {
        Queries.FindTripAsync("TR-0009999", Arg.Any<CancellationToken>()).Returns((TripDetailView?)null);

        var page = Render<TripDetailPage>(parameters => parameters.Add(detail => detail.Number, "TR-0009999"));

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("There is no Trip TR-0009999."));
    }

    [Fact]
    public void TheStops_ChangeAfterTheNextRead_WithoutARefresh()
    {
        Queries.FindTripAsync("TR-0000001", Arg.Any<CancellationToken>()).Returns(new TripDetailView(Trip with { StopCount = 0 }, [], [], []));
        var page = Render<TripDetailPage>(parameters => parameters.Add(detail => detail.Number, "TR-0000001"));
        page.WaitForElement("a[href='/trips/shipments']");
        Queries.FindTripAsync("TR-0000001", Arg.Any<CancellationToken>()).Returns(new TripDetailView(Trip, [Collect, Deliver], [], []));

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("[aria-label='Stops'] tbody tr").Should().HaveCount(2));
    }

    [Fact]
    public void ADraftsStops_CanBeMovedWithTheArrows_AndAreSentInTheNewOrder()
    {
        Queries.FindTripAsync("TR-0000001", Arg.Any<CancellationToken>()).Returns(new TripDetailView(Trip, [Collect, Deliver], [], []));
        Commands.ReorderTripStopsAsync("TR-0000001", Arg.Any<IReadOnlyList<StopOrder>>(), Arg.Any<CancellationToken>()).Returns(new TripAnswer.Done());
        var page = Render<TripDetailPage>(parameters => parameters.Add(detail => detail.Number, "TR-0000001"));

        page.WaitForElement("fluent-button[aria-label='Move stop 1 down']").Click();

        Commands.Received(1).ReorderTripStopsAsync(
            "TR-0000001",
            Arg.Is<IReadOnlyList<StopOrder>>(order => order.SequenceEqual(new[] { new StopOrder("SH-0000001", StopKind.Delivery), new StopOrder("SH-0000001", StopKind.Collection) })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ADraftsStops_CanBeDraggedOntoAnotherStop_AndAreSentInTheNewOrder()
    {
        Queries.FindTripAsync("TR-0000001", Arg.Any<CancellationToken>()).Returns(new TripDetailView(Trip, [Collect, Deliver], [], []));
        Commands.ReorderTripStopsAsync("TR-0000001", Arg.Any<IReadOnlyList<StopOrder>>(), Arg.Any<CancellationToken>()).Returns(new TripAnswer.Done());
        var page = Render<TripDetailPage>(parameters => parameters.Add(detail => detail.Number, "TR-0000001"));
        page.WaitForElements("table[aria-label='Stops'] tbody tr")[0].DragStart();
        page.FindAll("table[aria-label='Stops'] tbody tr")[1].Drop();

        Commands.Received(1).ReorderTripStopsAsync(
            "TR-0000001",
            Arg.Is<IReadOnlyList<StopOrder>>(order => order.Select(stop => stop.Kind).SequenceEqual(new[] { StopKind.Delivery, StopKind.Collection })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ARefusedMove_ShowsTheReason_AndTheStopsStayAsTheyWere()
    {
        Queries.FindTripAsync("TR-0000001", Arg.Any<CancellationToken>()).Returns(new TripDetailView(Trip, [Collect, Deliver], [], []));
        Commands.ReorderTripStopsAsync("TR-0000001", Arg.Any<IReadOnlyList<StopOrder>>(), Arg.Any<CancellationToken>())
            .Returns(new TripAnswer.Refused("Shipment SH-0000001's delivery can't come before its collection."));
        var page = Render<TripDetailPage>(parameters => parameters.Add(detail => detail.Number, "TR-0000001"));

        page.WaitForElement("fluent-button[aria-label='Move stop 1 down']").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("delivery can't come before its collection"));
        var rows = page.FindAll("table[aria-label='Stops'] tbody tr");
        rows[0].TextContent.Should().Contain("Collect");
    }

    [Fact]
    public void ADispatchedTripsStops_CantBeMoved()
    {
        Queries.FindTripAsync("TR-0000001", Arg.Any<CancellationToken>())
            .Returns(new TripDetailView(Trip with { Status = TripStatus.Dispatched }, [Collect, Deliver], [], []));

        var page = Render<TripDetailPage>(parameters => parameters.Add(detail => detail.Number, "TR-0000001"));

        page.WaitForElements("[aria-label='Stops'] tbody tr");
        page.FindAll("fluent-button[aria-label^='Move stop']").Should().BeEmpty();
    }
}
