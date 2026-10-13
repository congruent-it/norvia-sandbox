using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.DriverApp.Application.Reports;
using Norvia.DriverApp.Application.Trips;
using Norvia.DriverApp.Application.Trips.GetStop;
using Norvia.DriverApp.Web.Components.Pages;
using Norvia.DriverApp.Web.Drivers;

namespace Norvia.DriverApp.Web.UnitTests;

/// <summary>The Stop screen (P12): what the Driver reads of a Stop, and which reports it offers.</summary>
public sealed class StopScreenTests : BunitContext
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    public StopScreenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddSingleton(_mediator);
        Services.AddSingleton<ILogger<Stop>>(NullLogger<Stop>.Instance);
        Services.AddSingleton(Substitute.For<AntiforgeryStateProvider>());
    }

    private static readonly StopDto Details = new(
        1, StopKindDto.Collection, "SH-0000001", "TO-0000001", "SHP-001", new StopAddressDto("Waalhaven 1", "3087 BM", "Rotterdam", "NL"), null,
        [new StopGoodsLineDto("Pallet", 2, 120, null)], null);

    private static StopView View(StopKindDto kind = StopKindDto.Collection, StopDto? details = null, TripReportView? outcome = null, bool takenOff = false, bool canComplete = true, bool canDamage = true, string? blocked = null, params TripReportView[] reports) =>
        new("SH-0000001", kind, takenOff ? null : details ?? Details with { Kind = kind, Consignee = kind == StopKindDto.Delivery ? new StopConsigneeDto("Bakker BV", null, "010-1234567", null) : null },
            outcome, takenOff, canComplete, canDamage, blocked, reports);

    private IRenderedComponent<Stop> Render(StopView? stop, string kindSegment = "collection", string? problem = null, bool loadFails = false)
    {
        var query = loadFails
            ? _mediator.Send(Arg.Any<GetDriverStopQuery>(), Arg.Any<CancellationToken>()).Returns<DriverStopView?>(_ => throw new InvalidOperationException("The database is down."))
            : _mediator.Send(Arg.Any<GetDriverStopQuery>(), Arg.Any<CancellationToken>()).Returns(stop is null ? null : new DriverStopView("TR-0000012", 3, "In progress", new DateOnly(2026, 10, 13), stop));
        _ = query;
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(DriverClaims.DriverId, "DRV-0002")], "test")) };

        // The Problem comes from the address, as it does when the Driver app sends the Driver back to the Stop screen.
        Services.GetRequiredService<NavigationManager>().NavigateTo(problem is null ? "/trips/TR-0000012/stops/SH-0000001/collection" : $"/trips/TR-0000012/stops/SH-0000001/collection?problem={problem}");
        return Render<Stop>(parameters => parameters
            .AddCascadingValue(context)
            .Add(page => page.TripNumber, "TR-0000012")
            .Add(page => page.ShipmentNumber, "SH-0000001")
            .Add(page => page.KindSegment, kindSegment));
    }

    [Fact]
    public void ACollectionStop_ShowsTheShipmentShipperAddressAndGoods_AndOffersCollectedCollectionFailedAndDamage()
    {
        var page = Render(View());

        page.Markup.Should().Contain("Collect SH-0000001").And.Contain("TO-0000001").And.Contain("SHP-001").And.Contain("Waalhaven 1, 3087 BM Rotterdam, NL").And.Contain("2 × Pallet");
        page.Markup.Should().NotContain("Consignee");
        page.FindAll("form").Select(form => form.GetAttribute("action")).Should().Equal(
            "/trips/TR-0000012/stops/SH-0000001/collection/collected",
            "/trips/TR-0000012/stops/SH-0000001/collection/collection-failed",
            "/trips/TR-0000012/stops/SH-0000001/collection/damaged");
        page.FindAll("fluent-text-area").Should().OnlyContain(area => area.GetAttribute("name") == "remark" && area.HasAttribute("required") && area.GetAttribute("maxlength") == "500");
    }

    [Fact]
    public void ADeliveryStop_ShowsTheConsignee_AndOffersDeliverDeliveryFailedAndDamage()
    {
        var page = Render(View(StopKindDto.Delivery), "delivery");

        page.Markup.Should().Contain("Deliver SH-0000001").And.Contain("Consignee").And.Contain("Bakker BV").And.Contain("010-1234567");
        page.FindAll("form").Select(form => form.GetAttribute("action")).Should().Equal(
            "/trips/TR-0000012/stops/SH-0000001/delivery/deliver", "/trips/TR-0000012/stops/SH-0000001/delivery/delivery-failed", "/trips/TR-0000012/stops/SH-0000001/delivery/damaged");
    }

    [Fact]
    public void TheProofOfDeliveryForm_AsksForWhoReceivedTheCondition_ARemark_ASignatureDrawnOnScreenAndUpToFourPhotos()
    {
        var page = Render(View(StopKindDto.Delivery), "delivery");

        var form = page.FindAll("form").Single(form => form.GetAttribute("action")!.EndsWith("/delivery/deliver", StringComparison.Ordinal));
        form.GetAttribute("enctype").Should().Be("multipart/form-data");
        form.QuerySelector("input[name=receivedBy]")!.HasAttribute("required").Should().BeTrue();
        form.QuerySelectorAll("input[name=condition]").Select(input => input.GetAttribute("value")).Should().Equal("clean", "reservations");
        form.QuerySelector("input[name=condition][value=clean]")!.HasAttribute("checked").Should().BeTrue("goods are clean unless the Driver says otherwise");
        form.QuerySelector("textarea[name=remark]")!.GetAttribute("maxlength").Should().Be("500");
        form.QuerySelector("canvas[data-signature]").Should().NotBeNull();
        form.QuerySelector("input[type=hidden][name=signature]").Should().NotBeNull();
        var photos = form.QuerySelector("input[type=file][name=photos]")!;
        photos.GetAttribute("accept").Should().Be("image/jpeg,image/png");
        photos.HasAttribute("multiple").Should().BeTrue();
        form.QuerySelector("fluent-button[type=submit]")!.TextContent.Should().Contain("Deliver");
    }

    [Theory]
    [InlineData("receivedBy", "who received the goods")]
    [InlineData("condition", "clean or with reservations")]
    [InlineData("signature", "has to sign")]
    [InlineData("photos", "at most 4")]
    [InlineData("remark", "with reservations")]
    public void AProblemWithTheProofOfDeliveryForm_IsSaidInTheDriversWords(string problem, string words)
    {
        TripText.Problem(problem).Should().Contain(words);
    }

    [Fact]
    public void ABlockedStop_SaysWhy_AndOffersNothingToComplete()
    {
        var page = Render(View(StopKindDto.Delivery, canComplete: false, blocked: "Collect the Shipment first."), "delivery");

        page.Markup.Should().Contain("Collect the Shipment first.").And.NotContain("delivery-failed");
        page.FindAll("form").Select(form => form.GetAttribute("action")).Should().Equal("/trips/TR-0000012/stops/SH-0000001/delivery/damaged");
    }

    [Fact]
    public void ACompletedStop_SaysItIsDone_ShowsItsReports_AndTellsToReloadWhileTheTmsHasNotAnswered_WithoutAutoRefresh()
    {
        var sending = new TripReportView(Guid.NewGuid(), ReportKindDto.Collected, ReportStateDto.Sending, null, null, DateTimeOffset.UtcNow, "SH-0000001", StopKindDto.Collection);

        var page = Render(View(outcome: sending, canComplete: false, reports: sending));

        page.Markup.Should().Contain("This Stop is done.").And.Contain("reload the page").And.Contain("Collected SH-0000001").And.Contain("Sending");
        page.Markup.Should().NotContain("collection-failed").And.NotContain("http-equiv");
    }

    [Fact]
    public void AStopTakenOffByThePlanner_SaysSo_AndOffersNothing()
    {
        var recorded = new TripReportView(Guid.NewGuid(), ReportKindDto.Collected, ReportStateDto.Recorded, null, null, DateTimeOffset.UtcNow, "SH-0000001", StopKindDto.Collection);

        var page = Render(View(outcome: recorded, takenOff: true, canComplete: false, canDamage: false));

        page.Markup.Should().Contain("Taken off by the Planner, call the Planner").And.NotContain("Waalhaven");
        page.FindAll("form").Should().BeEmpty();
    }

    [Theory]
    [InlineData("remark", "a remark is required")]
    [InlineData("address", "no complete address")]
    public void AProblemOfTheLastPost_IsShownAsAMessage(string problem, string expected) =>
        Render(View(), problem: problem).Markup.Should().Contain(expected);

    [Fact]
    public void AStopThatIsNotTheDrivers_IsSaidNotToBeOnTheirTrips() =>
        Render(null).Markup.Should().Contain("isn't on one of your Trips");

    [Fact]
    public void AnUnknownKindOfStop_IsNotLookedUp()
    {
        var page = Render(View(), "pickup");

        page.Markup.Should().Contain("isn't on one of your Trips");
        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void WhenTheStopCannotBeLoaded_TheDriverGetsAMessage_NotAnErrorPage() =>
        Render(null, loadFails: true).Markup.Should().Contain("couldn't be loaded");
}
