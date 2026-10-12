using Norvia.PartnerPortal.Application.DriverAppFault;
using Norvia.PartnerPortal.Web.Presenter;

namespace Norvia.PartnerPortal.Web.UnitTests.Presenter;

public sealed class PresenterPanelTests : PortalWebContext
{
    private void Current(bool unavailable) =>
        Portal.GetDriverAppFaultAsync(Arg.Any<CancellationToken>()).Returns(new DriverAppFaultView(unavailable));

    [Fact]
    public void Render_WhenTheDriverAppIsAvailable_OffersToMakeItUnavailable()
    {
        Current(unavailable: false);

        var panel = Render<PresenterPanel>();

        panel.Find("[role=status]").TextContent.Should().Contain("available").And.NotContain("unavailable");
        panel.Find("fluent-button").TextContent.Trim().Should().Be("Make the Driver app unavailable");
    }

    [Fact]
    public void Render_WhenTheFaultIsOn_SaysSo_AndOffersToSwitchItOff()
    {
        Current(unavailable: true);

        var panel = Render<PresenterPanel>();

        panel.Find("[role=status]").TextContent.Should().Contain("unavailable");
        panel.Find("fluent-button").TextContent.Trim().Should().Be("Make the Driver app available");
    }

    [Fact]
    public void SwitchingTheFaultOn_SendsItToTheDriverApp_AndShowsTheNewState()
    {
        Current(unavailable: false);
        Portal.SetDriverAppFaultAsync(true, Arg.Any<CancellationToken>()).Returns(new DriverAppFaultView(true));
        var panel = Render<PresenterPanel>();

        panel.Find("fluent-button").Click();

        Portal.Received(1).SetDriverAppFaultAsync(true, Arg.Any<CancellationToken>());
        panel.Find("fluent-button").TextContent.Trim().Should().Be("Make the Driver app available");
    }

    [Fact]
    public void SwitchingTheFaultOff_SendsItToTheDriverApp()
    {
        Current(unavailable: true);
        Portal.SetDriverAppFaultAsync(false, Arg.Any<CancellationToken>()).Returns(new DriverAppFaultView(false));
        var panel = Render<PresenterPanel>();

        panel.Find("fluent-button").Click();

        Portal.Received(1).SetDriverAppFaultAsync(false, Arg.Any<CancellationToken>());
        panel.Find("fluent-button").TextContent.Trim().Should().Be("Make the Driver app unavailable");
    }

    [Fact]
    public void WhenTheDriverAppCantBeReached_SaysSo_AndOffersNoSwitch()
    {
        Portal.GetDriverAppFaultAsync(Arg.Any<CancellationToken>()).Returns<DriverAppFaultView>(_ => throw new HttpRequestException());

        var panel = Render<PresenterPanel>();

        panel.Find("[role=alert]").TextContent.Should().Contain("couldn't be reached");
        panel.FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void WhenSwitchingFails_SaysSo_AndKeepsTheOldState()
    {
        Current(unavailable: false);
        Portal.SetDriverAppFaultAsync(true, Arg.Any<CancellationToken>()).Returns<DriverAppFaultView>(_ => throw new HttpRequestException());
        var panel = Render<PresenterPanel>();

        panel.Find("fluent-button").Click();

        panel.Find("[role=alert]").TextContent.Should().Contain("couldn't be switched");
        panel.Find("fluent-button").TextContent.Trim().Should().Be("Make the Driver app unavailable");
    }
}
