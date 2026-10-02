using Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierBookings;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierBookings;

public sealed class AnswerModePanelTests : PortalWebContext
{
    private IRenderedComponent<AnswerModePanel> RenderPanel(string personaId = "kessler-spedition") =>
        Render<AnswerModePanel>(parameters => parameters.Add(panel => panel.PersonaId, personaId));

    private void Current(CarrierAnswerMode mode, CarrierRejectionReason reason = CarrierRejectionReason.NoCapacity, int seconds = 5) =>
        Portal.GetCarrierAnswerSettingsAsync("kessler-spedition", Arg.Any<CancellationToken>()).Returns(new CarrierAnswerSettingsView(mode, reason, seconds));

    [Fact]
    public void Render_ShowsTheModeTheCarrierHasNow()
    {
        Current(CarrierAnswerMode.AutoConfirm, seconds: 7);

        var panel = RenderPanel();

        Field(panel.Find("[role=group]"), "Mode").GetAttribute("value").Should().Be("AutoConfirm");
        Field(panel.Find("[role=group]"), "Delay before the answer (seconds)").GetAttribute("value").Should().Be("7");
    }

    [Fact]
    public void Render_ForAManualCarrier_ShowsNeitherADelayNorAReason()
    {
        Current(CarrierAnswerMode.Manual);

        var panel = RenderPanel();

        panel.FindAll("select").Should().ContainSingle();
        panel.FindAll("input").Should().BeEmpty();
    }

    [Fact]
    public void Render_ForAnAutoRejectingCarrier_AlsoAsksForTheReason()
    {
        Current(CarrierAnswerMode.AutoReject, CarrierRejectionReason.LaneNotServed);

        RenderPanel().FindAll("select").Should().HaveCount(2);
    }

    [Fact]
    public void ChoosingAutoReject_ShowsTheReason()
    {
        Current(CarrierAnswerMode.Manual);
        var panel = RenderPanel();

        Field(panel.Find("[role=group]"), "Mode").Change("AutoReject");

        panel.FindAll("select").Should().HaveCount(2);
    }

    [Fact]
    public void Save_SendsTheChosenModeReasonAndDelay()
    {
        Current(CarrierAnswerMode.Manual);
        Portal.ChangeCarrierAnswerSettingsAsync("kessler-spedition", Arg.Any<CarrierAnswerSettingsView>(), Arg.Any<CancellationToken>())
            .Returns(new CarrierAnswerSettingsView(CarrierAnswerMode.AutoReject, CarrierRejectionReason.GoodsNotAccepted, 9));
        var panel = RenderPanel();
        Field(panel.Find("[role=group]"), "Mode").Change("AutoReject");
        Field(panel.Find("[role=group]"), "Rejection reason").Change("GoodsNotAccepted");
        Field(panel.Find("[role=group]"), "Delay before the answer (seconds)").Change("9");

        panel.Find("form").Submit();

        Portal.Received(1).ChangeCarrierAnswerSettingsAsync(
            "kessler-spedition", new CarrierAnswerSettingsView(CarrierAnswerMode.AutoReject, CarrierRejectionReason.GoodsNotAccepted, 9), Arg.Any<CancellationToken>());
        panel.Find("[role=status]").TextContent.Should().Contain("Saved");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("121")]
    public void Save_WithADelayOutOfRange_SendsNothing(string seconds)
    {
        Current(CarrierAnswerMode.AutoConfirm);
        var panel = RenderPanel();
        Field(panel.Find("[role=group]"), "Delay before the answer (seconds)").Change(seconds);

        panel.Find("form").Submit();

        Portal.DidNotReceive().ChangeCarrierAnswerSettingsAsync(Arg.Any<string>(), Arg.Any<CarrierAnswerSettingsView>(), Arg.Any<CancellationToken>());
        panel.Markup.Should().Contain(AnswerModeForm.DelayOutOfRange);
    }

    [Fact]
    public void Save_WhenItFails_SaysSo()
    {
        Current(CarrierAnswerMode.AutoConfirm);
        Portal.ChangeCarrierAnswerSettingsAsync(Arg.Any<string>(), Arg.Any<CarrierAnswerSettingsView>(), Arg.Any<CancellationToken>())
            .Returns<CarrierAnswerSettingsView?>(_ => throw new InvalidOperationException("The database is down."));
        var panel = RenderPanel();

        panel.Find("form").Submit();

        panel.Find("[role=alert]").TextContent.Should().Contain("couldn't be saved");
    }

    [Fact]
    public void Save_ForANonexistentPersona_SaysSo()
    {
        Current(CarrierAnswerMode.AutoConfirm);
        Portal.ChangeCarrierAnswerSettingsAsync(Arg.Any<string>(), Arg.Any<CarrierAnswerSettingsView>(), Arg.Any<CancellationToken>()).Returns((CarrierAnswerSettingsView?)null);
        var panel = RenderPanel();

        panel.Find("form").Submit();

        panel.Find("[role=alert]").TextContent.Should().Contain("couldn't be saved");
    }

    [Fact]
    public void Render_WhenTheModeCantBeLoaded_SaysSo()
    {
        Portal.GetCarrierAnswerSettingsAsync("kessler-spedition", Arg.Any<CancellationToken>())
            .Returns<CarrierAnswerSettingsView?>(_ => throw new InvalidOperationException("The database is down."));

        RenderPanel().Find("[role=alert]").TextContent.Should().Contain("couldn't be loaded");
    }

    [Fact]
    public void Render_ForNoSuchCarrier_SaysTheModeCantBeLoaded()
    {
        Portal.GetCarrierAnswerSettingsAsync("kessler-spedition", Arg.Any<CancellationToken>()).Returns((CarrierAnswerSettingsView?)null);

        RenderPanel().Find("[role=alert]").TextContent.Should().Contain("couldn't be loaded");
    }

    [Fact]
    public void Render_WhileLoading_SaysSo()
    {
        Portal.GetCarrierAnswerSettingsAsync("kessler-spedition", Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<CarrierAnswerSettingsView?>().Task);

        RenderPanel().Find("[role=status]").TextContent.Should().Contain("Loading");
    }
}
