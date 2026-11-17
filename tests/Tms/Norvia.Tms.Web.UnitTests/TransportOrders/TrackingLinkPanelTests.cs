using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

/// <summary>The Planner reads the Consignee's tracking link and replaces it when it went to the wrong person (ADR 0052).</summary>
public sealed class TrackingLinkPanelTests : TmsWebContext
{
    public TrackingLinkPanelTests() =>
        Services.AddSingleton(Options.Create(new PartnerPortalLinkOptions { BaseAddress = "https://portal.example/" }));

    [Fact]
    public void Render_ShowsTheLinkOfTheCurrentCode()
    {
        Commands.GetTrackingCodeAsync("TO-0000007", Arg.Any<CancellationToken>()).Returns(new TrackingCodeAnswer.Shown("0123456789ABCDEF"));

        var panel = RenderPanel();

        panel.Find("[data-testid=tracking-link]").TextContent.Should().Be("https://portal.example/track/0123456789ABCDEF");
    }

    [Fact]
    public void Render_AnOrderFromBeforeCodesExisted_SaysItHasNoLinkYet()
    {
        Commands.GetTrackingCodeAsync("TO-0000007", Arg.Any<CancellationToken>()).Returns(new TrackingCodeAnswer.Shown(null));

        RenderPanel().Find("[data-testid=tracking-link-none]").TextContent.Should().Contain("no tracking link yet");
    }

    [Fact]
    public void Replace_ShowsTheNewLink()
    {
        Commands.GetTrackingCodeAsync("TO-0000007", Arg.Any<CancellationToken>()).Returns(new TrackingCodeAnswer.Shown("0123456789ABCDEF"));
        Commands.ReplaceTrackingCodeAsync("TO-0000007", Arg.Any<CancellationToken>()).Returns(new TrackingCodeAnswer.Shown("FEDCBA9876543210"));
        var panel = RenderPanel();

        panel.Find("[data-testid=replace-tracking-code]").Click();

        panel.Find("[data-testid=tracking-link]").TextContent.Should().EndWith("/track/FEDCBA9876543210");
    }

    [Fact]
    public void Replace_WhenTheTmsRefuses_ShowsAWarning_AndKeepsTheLink()
    {
        Commands.GetTrackingCodeAsync("TO-0000007", Arg.Any<CancellationToken>()).Returns(new TrackingCodeAnswer.Shown("0123456789ABCDEF"));
        Commands.ReplaceTrackingCodeAsync("TO-0000007", Arg.Any<CancellationToken>()).Returns(new TrackingCodeAnswer.NotFound());
        var panel = RenderPanel();

        panel.Find("[data-testid=replace-tracking-code]").Click();

        panel.Find("[role=alert]").TextContent.Should().Contain("no tracking link for Transport Order TO-0000007");
    }

    [Fact]
    public void Replace_WhenTheCallFails_SaysSo()
    {
        Commands.GetTrackingCodeAsync("TO-0000007", Arg.Any<CancellationToken>()).Returns(new TrackingCodeAnswer.Shown("0123456789ABCDEF"));
        Commands.ReplaceTrackingCodeAsync("TO-0000007", Arg.Any<CancellationToken>()).Returns<TrackingCodeAnswer>(_ => throw new HttpRequestException("down"));
        var panel = RenderPanel();

        panel.Find("[data-testid=replace-tracking-code]").Click();

        panel.Find("[role=alert]").TextContent.Should().Contain("couldn't be replaced");
    }

    [Fact]
    public void Render_WhenTheCodeCantBeRead_SaysSo()
    {
        Commands.GetTrackingCodeAsync("TO-0000007", Arg.Any<CancellationToken>()).Returns<TrackingCodeAnswer>(_ => throw new HttpRequestException("down"));

        RenderPanel().Find("[role=alert]").TextContent.Should().Contain("couldn't be loaded");
    }

    private IRenderedComponent<TrackingLinkPanel> RenderPanel() =>
        Render<TrackingLinkPanel>(parameters => parameters.Add(panel => panel.TransportOrderNumber, "TO-0000007"));
}
