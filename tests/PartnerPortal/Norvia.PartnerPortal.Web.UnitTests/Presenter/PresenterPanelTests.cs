using Norvia.PartnerPortal.Application.DriverSignIns;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Web.Presenter;

namespace Norvia.PartnerPortal.Web.UnitTests.Presenter;

public sealed class PresenterPanelTests : PortalWebContext
{
    private static readonly FaultView Off = new("driver-app-unavailable", "driver-app", "The Driver app refuses every Trip.", false, null);

    private void Current(params FaultView[] faults) =>
        Portal.ListFaultsAsync(Arg.Any<CancellationToken>()).Returns(faults);

    private FaultView OnFor(TimeSpan left) => Off with { On = true, Until = Time.GetUtcNow() + left };

    [Fact]
    public void Render_ListsEveryFault_WithItsOwner_AndDescription()
    {
        Current(Off, new FaultView("tms-slow", "tms", "The TMS answers slowly.", false, null));

        var panel = Render<PresenterPanel>();

        var faults = panel.FindAll("[aria-label='Faults'] [role=group]");
        faults.Select(fault => fault.GetAttribute("aria-label")).Should().Equal("driver-app-unavailable", "tms-slow");
        faults[0].TextContent.Should().Contain("driver-app").And.Contain("The Driver app refuses every Trip.");
    }

    [Fact]
    public void Render_WhenAFaultIsOff_SaysSo_AndOffersOnButNotOff()
    {
        Current(Off);

        var fault = Render<PresenterPanel>().Find("[role=group][aria-label='driver-app-unavailable']");

        fault.QuerySelector("[role=status]")!.TextContent.Trim().Should().Be("Off");
        fault.QuerySelector("fluent-button[aria-label='Switch on driver-app-unavailable']").Should().NotBeNull();
        fault.QuerySelector("fluent-button[aria-label='Switch off driver-app-unavailable']")!.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Render_WhenAFaultIsOn_ShowsTheRemainingTime()
    {
        Current(OnFor(TimeSpan.FromSeconds(95)));

        var fault = Render<PresenterPanel>().Find("[role=group][aria-label='driver-app-unavailable']");

        fault.QuerySelector("[role=status]")!.TextContent.Should().Contain("On").And.Contain("1:35 left");
        fault.QuerySelector("fluent-button[aria-label='Switch off driver-app-unavailable']")!.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void TheRemainingTime_CountsDown_AndTheFaultShowsOffWhenItRunsOut()
    {
        Current(OnFor(TimeSpan.FromSeconds(2)));
        var panel = Render<PresenterPanel>();

        Time.Advance(TimeSpan.FromSeconds(1));
        panel.WaitForAssertion(() => panel.Find("[aria-label='driver-app-unavailable'] [role=status]").TextContent.Should().Contain("0:01 left"));
        Time.Advance(TimeSpan.FromSeconds(1));

        panel.WaitForAssertion(() => panel.Find("[aria-label='driver-app-unavailable'] [role=status]").TextContent.Trim().Should().Be("Off"));
    }

    [Fact]
    public void TheFaults_AreReadAgainEveryFewSeconds()
    {
        Current(Off);
        var panel = Render<PresenterPanel>();
        Current(OnFor(TimeSpan.FromSeconds(60)));

        Time.Advance(TimeSpan.FromSeconds(10));

        panel.WaitForAssertion(() => panel.Find("[aria-label='driver-app-unavailable'] [role=status]").TextContent.Should().Contain("On"));
    }

    [Fact]
    public void SwitchingAFaultOn_SendsTheChosenDuration_AndShowsTheNewState()
    {
        Current(Off);
        Portal.SwitchFaultAsync("driver-app", "driver-app-unavailable", Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(_ => OnFor(TimeSpan.FromSeconds(120)));
        var panel = Render<PresenterPanel>();

        panel.Find("fluent-button[aria-label='Switch on driver-app-unavailable']").Click();

        // The default is two minutes: a fault that is on is always switched on for a time.
        Portal.Received(1).SwitchFaultAsync("driver-app", "driver-app-unavailable", TimeSpan.FromSeconds(120), Arg.Any<CancellationToken>());
        panel.Find("[aria-label='driver-app-unavailable'] [role=status]").TextContent.Should().Contain("On").And.Contain("2:00 left");
    }

    [Fact]
    public void SwitchingAFaultOff_SendsNoDuration_AndShowsItOff()
    {
        Current(OnFor(TimeSpan.FromSeconds(60)));
        Portal.SwitchFaultAsync("driver-app", "driver-app-unavailable", null, Arg.Any<CancellationToken>()).Returns(Off);
        var panel = Render<PresenterPanel>();

        panel.Find("fluent-button[aria-label='Switch off driver-app-unavailable']").Click();

        Portal.Received(1).SwitchFaultAsync("driver-app", "driver-app-unavailable", null, Arg.Any<CancellationToken>());
        panel.Find("[aria-label='driver-app-unavailable'] [role=status]").TextContent.Trim().Should().Be("Off");
    }

    [Fact]
    public void WhenTheFaultsCantBeLoaded_SaysSo_AndOffersNoSwitch()
    {
        Portal.ListFaultsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<FaultView>>(_ => throw new HttpRequestException());

        var panel = Render<PresenterPanel>();

        panel.Find("[aria-label='Faults'] [role=alert]").TextContent.Should().Contain("couldn't be loaded");
        panel.FindAll("[aria-label='Faults'] fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void WhenNoApplicationOwnsAFault_SaysSo()
    {
        Current();

        Render<PresenterPanel>().Find("[aria-label='Faults'] [role=status]").TextContent.Should().Contain("No application owns a fault");
    }

    [Fact]
    public void WhenSwitchingFails_SaysSo_AndKeepsTheOldState()
    {
        Current(Off);
        Portal.SwitchFaultAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns<FaultView>(_ => throw new HttpRequestException());
        var panel = Render<PresenterPanel>();

        panel.Find("fluent-button[aria-label='Switch on driver-app-unavailable']").Click();

        panel.Find("[aria-label='Faults'] [role=alert]").TextContent.Should().Contain("couldn't be switched");
        panel.Find("[aria-label='driver-app-unavailable'] [role=status]").TextContent.Trim().Should().Be("Off");
    }

    [Fact]
    public void Render_WithEntraTestUsers_ListsEachDriversUserName_AndThePassword()
    {
        Current(Off);
        Portal.ListDriverSignInsAsync(Arg.Any<CancellationToken>()).Returns(new DriverSignInsView(
            "s3cret",
            [new DriverSignInView("DRV-0001", "Anouk de Vries", "drv-0001@norvia.example"), new DriverSignInView("DRV-0002", "Joost Bakker", "drv-0002@norvia.example")]));

        var panel = Render<PresenterPanel>();

        var signIn = panel.Find("[aria-label='Driver sign-in']");
        signIn.QuerySelectorAll("tbody tr").Select(row => row.TextContent).Should().Equal(
            "Anouk de VriesDRV-0001drv-0001@norvia.example", "Joost BakkerDRV-0002drv-0002@norvia.example");
        signIn.TextContent.Should().Contain("s3cret");
    }

    [Fact]
    public void Render_WithoutEntraTestUsers_SaysTheDriverAppsSignInPageStandsIn_AndListsTheDrivers()
    {
        Current(Off);
        Portal.ListDriverSignInsAsync(Arg.Any<CancellationToken>()).Returns(new DriverSignInsView(null, [new DriverSignInView("DRV-0001", "Anouk de Vries", null)]));

        var signIn = Render<PresenterPanel>().Find("[aria-label='Driver sign-in']");

        signIn.TextContent.Should().Contain("stands in").And.Contain("Anouk de Vries (DRV-0001)");
        signIn.QuerySelector("table").Should().BeNull();
    }

    [Fact]
    public void Render_WhenTheSignInDetailsCantBeLoaded_SaysSo_AndTheFaultStillWorks()
    {
        Current(Off);
        Portal.ListDriverSignInsAsync(Arg.Any<CancellationToken>()).Returns<DriverSignInsView>(_ => throw new InvalidDataException());

        var panel = Render<PresenterPanel>();

        panel.Find("[aria-label='Driver sign-in'] [role=alert]").TextContent.Should().Contain("sign-in details couldn't be loaded");
        panel.Find("fluent-button[aria-label='Switch on driver-app-unavailable']").Should().NotBeNull();
    }
}
