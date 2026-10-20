using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Erp.Application.Operators;
using Norvia.Erp.Web.Components.Layout;
using Norvia.Erp.Web.Components.Pages;
using Norvia.Erp.Web.Operators;

namespace Norvia.Erp.Web.UnitTests;

public sealed class LayoutTests : ErpWebContext
{
    [Fact]
    public void Render_ForFinance_ShowsWhoTheyAreTheThreeSectionsAndTheViewsOfTheOpenOne()
    {
        var layout = RenderLayout();

        layout.Find("nav").TextContent.Should().Contain("Dev Eloper");
        var sections = layout.FindAll("nav[aria-label=Main] a");
        sections.Select(link => link.TextContent.Trim()).Should().Equal("Billing", "Carrier invoicing", "Master data");
        sections.Select(link => link.GetAttribute("href")).Should().Equal("/billable-lines", "/carrier-costs", "/");
        var views = layout.FindAll("nav:not([aria-label=Main]) a");
        views.Select(link => link.TextContent.Trim()).Should().Equal("Shippers", "Carriers", "Rate Cards", "Carrier Rate Cards"); // the test's address is the Shippers list
        layout.Find("main").TextContent.Should().Contain("the page");
    }

    [Theory]
    [InlineData("/", "Master data", "Shippers, Carriers, Rate Cards, Carrier Rate Cards")]
    [InlineData("/carriers", "Master data", "Shippers, Carriers, Rate Cards, Carrier Rate Cards")]
    [InlineData("/shipper-invoices/INV-2030-000001", "Billing", "Waiting billable lines, Shipper invoices")]
    [InlineData("/carrier-costs", "Carrier invoicing", "Open carrier costs, Carrier invoices to decide, Carrier invoices")]
    [InlineData("/carrier-invoices/to-decide", "Carrier invoicing", "Open carrier costs, Carrier invoices to decide, Carrier invoices")]
    [InlineData("/carrier-invoices?x=1", "Carrier invoicing", "Open carrier costs, Carrier invoices to decide, Carrier invoices")]
    [InlineData("/unknown", "Billing", "Waiting billable lines, Shipper invoices")]
    public void Render_ShowsTheViewsOfTheSectionTheOpenPageIsIn(string path, string section, string views)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);

        var layout = RenderLayout();

        layout.Find("nav:not([aria-label=Main])").GetAttribute("aria-label").Should().Be(section);
        layout.Find("nav[aria-label=Main] a[aria-current=true]").TextContent.Trim().Should().Be(section);
        layout.FindAll("nav:not([aria-label=Main]) a").Select(link => link.TextContent.Trim()).Should().Equal(views.Split(", "));
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
