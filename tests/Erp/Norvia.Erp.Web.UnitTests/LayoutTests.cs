using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Norvia.Erp.Application.Operators;
using Norvia.Erp.Web.Components.Layout;
using Norvia.Erp.Web.Components.Pages;
using Norvia.Erp.Web.Operators;

namespace Norvia.Erp.Web.UnitTests;

public sealed class LayoutTests : ErpWebContext
{
    [Fact]
    public void Render_ForFinance_ShowsWhoTheyAreTheBillingListsAndTheFourMasterDataLists()
    {
        var layout = RenderLayout();

        layout.Find("nav").TextContent.Should().Contain("Dev Eloper");
        var links = layout.FindAll("nav a");
        links.Select(link => link.TextContent.Trim()).Should().Equal("Waiting billable lines", "Shipper invoices", "Shippers", "Carriers", "Rate Cards", "Carrier Rate Cards");
        links.Select(link => link.GetAttribute("href")).Should().Equal("/billable-lines", "/shipper-invoices", "/", "/carriers", "/rate-cards", "/carrier-rate-cards");
        layout.Find("main").TextContent.Should().Contain("the page");
    }

    [Fact]
    public void Render_ForNobodySignedIn_ShowsNoUser()
    {
        User.SetNotAuthorized();

        RenderLayout().Find("nav").TextContent.Should().NotContain("Dev Eloper");
    }

    [Fact]
    public void Render_OffersNoEditAction()
    {
        RenderLayout().FindAll("button, form, input").Should().BeEmpty();
    }

    [Fact]
    public void RenderErrorPage_AfterAFailure_ShowsTheRequestIdWithoutDetailsOfTheFailure()
    {
        var page = Render<Error>(parameters => parameters.AddCascadingValue(new DefaultHttpContext { TraceIdentifier = "trace-1" }));

        page.Find("h1").TextContent.Should().Be("Something went wrong");
        page.Markup.Should().Contain("trace-1").And.NotContain("Exception");
    }

    [Fact]
    public void RenderNotFoundPage_ForAnUnknownAddress_LeadsBackToTheShippers()
    {
        Render<NotFound>().Find("a").GetAttribute("href").Should().Be("/");
    }

    [Fact]
    public void RenderRefused_ForAUserWithoutTheRole_NamesTheRoleAndAnswersForbidden()
    {
        User.SetAuthorized("Iris Planner").SetRoles();
        var request = new DefaultHttpContext();

        var page = Render<Refused>(parameters => parameters.AddCascadingValue(request));

        page.Find("h1").TextContent.Should().Be("You can't use the ERP");
        page.Markup.Should().Contain("Iris Planner").And.Contain(ErpRoles.Finance);
        request.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, (RenderFragment)(builder => builder.AddContent(0, "the page"))));
}
