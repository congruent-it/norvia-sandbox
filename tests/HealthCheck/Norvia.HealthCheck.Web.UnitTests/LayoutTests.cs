using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Norvia.HealthCheck.Web.Components.Layout;
using Norvia.HealthCheck.Web.Components.Pages;

namespace Norvia.HealthCheck.Web.UnitTests;

public sealed class LayoutTests : HealthCheckWebContext
{
    [Fact]
    public void Render_ForAnOperator_ShowsWhoTheyAre()
    {
        Operator.SetAuthorized("Iris Operator");

        var layout = RenderLayout();

        layout.Find("nav").TextContent.Should().Contain("Iris Operator");
        layout.Find("main").TextContent.Should().Contain("the page");
    }

    [Fact]
    public void Render_ForNobodySignedIn_ShowsNoOperator()
    {
        var layout = RenderLayout();

        layout.Find("nav").TextContent.Trim().Should().Be("Estate health check");
    }

    [Fact]
    public void RenderErrorPage_AfterAFailure_ShowsTheRequestIdWithoutDetailsOfTheFailure()
    {
        var page = Render<Error>(parameters => parameters.AddCascadingValue(new DefaultHttpContext { TraceIdentifier = "trace-1" }));

        page.Find("h1").TextContent.Should().Be("Something went wrong");
        page.Markup.Should().Contain("trace-1").And.NotContain("Exception");
    }

    [Fact]
    public void RenderErrorPage_WithoutARequest_ShowsNoRequestId()
    {
        var page = Render<Error>();

        page.Markup.Should().NotContain("Request ID");
    }

    [Fact]
    public void RenderNotFoundPage_PointsBackToTheHealthCheck()
    {
        var page = Render<NotFound>();

        page.Find("h1").TextContent.Should().Be("Page not found");
        page.Find("a").GetAttribute("href").Should().Be("/");
    }

    [Fact]
    public void PageTitles_NameTheAppFirst()
    {
        PageTitles.Run.Should().StartWith("Estate health check");
        PageTitles.Refused.Should().StartWith("Estate health check");
    }

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, (RenderFragment)(builder => builder.AddContent(0, "the page"))));
}
