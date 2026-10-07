using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Norvia.PartnerPortal.Web.Components.Layout;
using Norvia.PartnerPortal.Web.Components.Pages;

namespace Norvia.PartnerPortal.Web.UnitTests;

public sealed class LayoutTests : PortalWebContext
{
    [Fact]
    public void MainLayout_OnAShippersPage_NavigatesAsThatPersona_AndShowsItAsPlayed()
    {
        var context = new DefaultHttpContext();
        context.Request.RouteValues["PersonaId"] = "brouwer-dranken";

        var layout = Render<MainLayout>(parameters => parameters
            .AddCascadingValue(context)
            .Add(component => component.Body, (RenderFragment)(builder => builder.AddContent(0, "the page"))));

        layout.FindAll("nav a").Select(link => link.GetAttribute("href")).Should().Contain(["/personas/brouwer-dranken/bookings/new", "/personas/brouwer-dranken/bookings", "/personas/brouwer-dranken/tracking", "/personas/brouwer-dranken/notifications"]);
        layout.Find("button[aria-pressed=true]").TextContent.Trim().Should().Be("Brouwer Dranken");
        layout.Find("main").TextContent.Should().Contain("the page");
    }

    [Fact]
    public void MainLayout_OnACarriersPage_NavigatesToItsInbox_AndOffersNoBooking()
    {
        var context = new DefaultHttpContext();
        context.Request.RouteValues["PersonaId"] = "van-dijk-transport";

        var layout = Render<MainLayout>(parameters => parameters
            .AddCascadingValue(context)
            .Add(component => component.Body, (RenderFragment)(builder => builder.AddContent(0, "the page"))));

        layout.FindAll("nav a").Select(link => link.GetAttribute("href")).Should().Contain("/personas/van-dijk-transport/booking-requests")
            .And.Contain("/personas/van-dijk-transport/shipments")
            .And.NotContain("/personas/van-dijk-transport/bookings/new")
            .And.NotContain("/personas/van-dijk-transport/notifications")
            .And.NotContain("/personas/van-dijk-transport/tracking");
        layout.Find("button[aria-pressed=true]").TextContent.Trim().Should().Be("Van Dijk Transport");
    }

    [Fact]
    public void MainLayout_WhenThePersonasCantBeRead_StillShowsThePage_WithoutTheSwitcher()
    {
        Portal.ListPersonasAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Norvia.PartnerPortal.Application.Personas.PersonaView>>(_ => throw new InvalidOperationException("The seed file is gone."));

        var layout = Render<MainLayout>(parameters => parameters
            .AddCascadingValue(new DefaultHttpContext())
            .Add(component => component.Body, (RenderFragment)(builder => builder.AddContent(0, "the page"))));

        layout.Find("main").TextContent.Should().Contain("the page");
        layout.FindAll("form[aria-label=Persona]").Should().BeEmpty();
    }

    [Fact]
    public void ErrorPage_ShowsTheRequestId()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-1" };

        var page = Render<Error>(parameters => parameters.AddCascadingValue(context));

        page.Markup.Should().Contain("trace-1");
    }
}
