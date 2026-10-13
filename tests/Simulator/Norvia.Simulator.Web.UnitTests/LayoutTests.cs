using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Norvia.Simulator.Application.Operators;
using Norvia.Simulator.Web.Components.Layout;
using Norvia.Simulator.Web.Components.Pages;
using Norvia.Simulator.Web.Operators;

namespace Norvia.Simulator.Web.UnitTests;

public sealed class LayoutTests : SimulatorWebContext
{
    [Fact]
    public void Render_ForTheLocalOperator_ShowsWhoTheyAre()
    {
        var layout = RenderLayout();

        layout.Find("nav").TextContent.Should().Contain("Dev Eloper");
        layout.FindAll("nav a").Select(link => link.TextContent.Trim()).Should().Equal("Simulator", "Configure");
        layout.Find("main").TextContent.Should().Contain("the page");
    }

    [Fact]
    public void Render_ForNobodySignedIn_ShowsNoOperator()
    {
        Operator.SetNotAuthorized();

        RenderLayout().Find("nav").TextContent.Should().NotContain("Dev Eloper");
    }

    [Fact]
    public void RenderErrorPage_AfterAFailure_ShowsTheRequestIdWithoutDetailsOfTheFailure()
    {
        var page = Render<Error>(parameters => parameters.AddCascadingValue(new DefaultHttpContext { TraceIdentifier = "trace-1" }));

        page.Find("h1").TextContent.Should().Be("Something went wrong");
        page.Markup.Should().Contain("trace-1").And.NotContain("Exception");
    }

    [Fact]
    public void RenderNotFoundPage_ForAnUnknownAddress_LeadsBackToConfigure()
    {
        Render<NotFound>().Find("a").GetAttribute("href").Should().Be("/");
    }

    [Fact]
    public void RenderRefused_ForAnOperatorWithoutTheRole_NamesTheRolesAndAnswersForbidden()
    {
        Operator.SetAuthorized("Iris Operator").SetRoles();
        var request = new DefaultHttpContext();

        var page = Render<Refused>(parameters => parameters.AddCascadingValue(request));

        page.Find("h1").TextContent.Should().Be("You can't use the Simulator");
        page.Markup.Should().Contain("Iris Operator").And.Contain(OperatorRoles.Run).And.Contain(OperatorRoles.Read);
        request.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, (RenderFragment)(builder => builder.AddContent(0, "the page"))));
}
