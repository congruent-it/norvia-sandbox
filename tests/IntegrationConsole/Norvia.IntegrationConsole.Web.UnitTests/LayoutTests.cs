using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Norvia.IntegrationConsole.Web.Components.Layout;
using Norvia.IntegrationConsole.Web.Components.Pages;
using Norvia.IntegrationConsole.Web.Operators;

namespace Norvia.IntegrationConsole.Web.UnitTests;

public sealed class LayoutTests : ConsoleWebContext
{
    [Fact]
    public void Render_ForAnOperatorSignedInWithEntra_ShowsWhoTheyAre_AndLetsThemSignOut()
    {
        Operator.SetAuthorized("Iris Operator").SetAuthenticationType(EasyAuthHandler.EntraAuthenticationType);

        var layout = RenderLayout();

        layout.Find("nav").TextContent.Should().Contain("Iris Operator");
        layout.FindAll("nav a").Should().Contain(link => link.GetAttribute("href") == "/.auth/logout" && link.TextContent.Trim() == "Sign out");
        layout.Find("main").TextContent.Should().Contain("the page");
    }

    [Fact]
    public void Render_ForTheLocalOperator_OffersNoSignOut()
    {
        // Locally there is no Easy Auth, so no endpoint to sign out at.
        Operator.SetAuthorized("Dev Eloper").SetAuthenticationType("LocalOperator");

        var layout = RenderLayout();

        layout.Find("nav").TextContent.Should().Contain("Dev Eloper");
        layout.FindAll("nav a").Should().NotContain(link => link.TextContent.Trim() == "Sign out");
    }

    [Fact]
    public void Render_ForNobodySignedIn_ShowsNoOperator()
    {
        var layout = RenderLayout();

        layout.FindAll("nav a").Select(link => link.TextContent.Trim()).Should().Equal("Integration console", "Search", "Failed runs", "Dead-letter queues");
    }

    [Fact]
    public void RenderErrorPage_AfterAFailure_ShowsTheRequestIdWithoutDetailsOfTheFailure()
    {
        var page = Render<Error>(parameters => parameters.AddCascadingValue(new DefaultHttpContext { TraceIdentifier = "trace-1" }));

        page.Find("h1").TextContent.Should().Be("Something went wrong");
        page.Markup.Should().Contain("trace-1").And.NotContain("Exception");
    }

    [Fact]
    public void RenderNotFoundPage_ForAnUnknownAddress_LeadsBackToSearch()
    {
        var page = Render<NotFound>();

        page.Find("a").GetAttribute("href").Should().Be("/");
    }

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(parameters => parameters
            .Add(layout => layout.Body, (RenderFragment)(builder => builder.AddContent(0, "the page"))));
}
