using Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.CarrierShipments;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierShipments;

public sealed class TrackingModePanelTests : PortalWebContext
{
    private IRenderedComponent<TrackingModePanel> RenderPanel(string personaId = "kessler-spedition") =>
        Render<TrackingModePanel>(parameters => parameters.Add(panel => panel.PersonaId, personaId));

    private void Current(TrackingMode mode, int seconds = 5) =>
        Portal.GetCarrierTrackingSettingsAsync(new PersonaId("kessler-spedition"), Arg.Any<CancellationToken>()).Returns(new CarrierTrackingSettingsView(mode, seconds));

    [Fact]
    public void Render_ShowsTheModeTheCarrierHasNow()
    {
        Current(TrackingMode.AutoProgress, seconds: 7);

        var panel = RenderPanel();

        Field(panel.Find("[role=group]"), "Mode").GetAttribute("value").Should().Be("AutoProgress");
        Field(panel.Find("[role=group]"), "Delay between Tracking events (seconds)").GetAttribute("value").Should().Be("7");
    }

    [Fact]
    public void Render_ForAManualCarrier_ShowsNoDelay()
    {
        Current(TrackingMode.Manual);

        var panel = RenderPanel();

        panel.FindAll("select").Should().ContainSingle();
        panel.FindAll("input").Should().BeEmpty();
    }

    [Fact]
    public void Save_SendsTheChosenModeAndDelay()
    {
        Current(TrackingMode.Manual);
        Portal.ChangeCarrierTrackingSettingsAsync(new PersonaId("kessler-spedition"), Arg.Any<CarrierTrackingSettingsView>(), Arg.Any<CancellationToken>())
            .Returns(new CarrierTrackingSettingsView(TrackingMode.AutoProgress, 9));
        var panel = RenderPanel();
        Field(panel.Find("[role=group]"), "Mode").Change("AutoProgress");
        Field(panel.Find("[role=group]"), "Delay between Tracking events (seconds)").Change("9");

        panel.Find("form").Submit();

        Portal.Received(1).ChangeCarrierTrackingSettingsAsync(
            new PersonaId("kessler-spedition"), new CarrierTrackingSettingsView(TrackingMode.AutoProgress, 9), Arg.Any<CancellationToken>());
        panel.Find("[role=status]").TextContent.Should().Contain("Saved");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("121")]
    public void Save_WithADelayOutOfRange_SendsNothing(string seconds)
    {
        Current(TrackingMode.AutoProgress);
        var panel = RenderPanel();
        Field(panel.Find("[role=group]"), "Delay between Tracking events (seconds)").Change(seconds);

        panel.Find("form").Submit();

        Portal.DidNotReceive().ChangeCarrierTrackingSettingsAsync(Arg.Any<PersonaId>(), Arg.Any<CarrierTrackingSettingsView>(), Arg.Any<CancellationToken>());
        panel.Markup.Should().Contain(TrackingModeForm.DelayOutOfRange);
    }

    [Fact]
    public void Save_WhenItFails_SaysSo()
    {
        Current(TrackingMode.AutoProgress);
        Portal.ChangeCarrierTrackingSettingsAsync(Arg.Any<PersonaId>(), Arg.Any<CarrierTrackingSettingsView>(), Arg.Any<CancellationToken>())
            .Returns<CarrierTrackingSettingsView?>(_ => throw new InvalidOperationException("The database is down."));
        var panel = RenderPanel();

        panel.Find("form").Submit();

        panel.Find("[role=alert]").TextContent.Should().Contain("couldn't be saved");
    }

    [Fact]
    public void Save_ForANonexistentPersona_SaysSo()
    {
        Current(TrackingMode.AutoProgress);
        Portal.ChangeCarrierTrackingSettingsAsync(Arg.Any<PersonaId>(), Arg.Any<CarrierTrackingSettingsView>(), Arg.Any<CancellationToken>()).Returns((CarrierTrackingSettingsView?)null);
        var panel = RenderPanel();

        panel.Find("form").Submit();

        panel.Find("[role=alert]").TextContent.Should().Contain("couldn't be saved");
    }

    [Fact]
    public void Render_WhenTheModeCantBeLoaded_SaysSo()
    {
        Portal.GetCarrierTrackingSettingsAsync(new PersonaId("kessler-spedition"), Arg.Any<CancellationToken>())
            .Returns<CarrierTrackingSettingsView?>(_ => throw new InvalidOperationException("The database is down."));

        RenderPanel().Find("[role=alert]").TextContent.Should().Contain("couldn't be loaded");
    }

    [Fact]
    public void Render_ForNoSuchCarrier_SaysTheModeCantBeLoaded()
    {
        Portal.GetCarrierTrackingSettingsAsync(new PersonaId("kessler-spedition"), Arg.Any<CancellationToken>()).Returns((CarrierTrackingSettingsView?)null);

        RenderPanel().Find("[role=alert]").TextContent.Should().Contain("couldn't be loaded");
    }

    [Fact]
    public void Render_WhileLoading_SaysSo()
    {
        Portal.GetCarrierTrackingSettingsAsync(new PersonaId("kessler-spedition"), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<CarrierTrackingSettingsView?>().Task);

        RenderPanel().Find("[role=status]").TextContent.Should().Contain("Loading");
    }
}
