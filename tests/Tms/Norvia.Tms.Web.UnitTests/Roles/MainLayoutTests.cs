using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Tms.Web.Components.Layout;
using Norvia.Tms.Web.Roles;

namespace Norvia.Tms.Web.UnitTests.Roles;

public sealed class MainLayoutTests : BunitContext
{
    public MainLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    [Fact]
    public void Render_AsCustomerService_LinksToTheRejectedBookingsTheRatingExceptionsAndNotYetBillable()
    {
        var layout = RenderAs("customer-service");

        MainNavigation(layout).Should().Equal(("/rejected-bookings", "Rejected bookings"), ("/transport-orders/all", "Transport Orders"), ("/rating-exceptions", "Rating exceptions"), ("/not-yet-billable", "Not yet billable"));
    }

    [Fact]
    public void Render_AsThePlanner_LinksToHomeAndGroupsTheViewsUnderPlanFollowAndOwnFleet()
    {
        var layout = RenderAs("planner");

        layout.FindAll("nav[aria-label=Main] > a").Skip(1).Select(link => (link.GetAttribute("href"), link.TextContent.Trim()))
            .Should().Equal(("/home", "Home"));
        layout.FindAll("nav[aria-label=Main] > details > summary").Select(summary => summary.TextContent.Trim())
            .Should().Equal("Plan", "Follow", "Own fleet");
        GroupLinks(layout, "Plan").Should().Equal(("/transport-orders", "Waiting for planning"), ("/transport-orders/all", "Transport Orders"), ("/shipments/to-release", "Shipments to release"));
        GroupLinks(layout, "Follow").Should().Equal(("/shipments/awaiting-answer", "Bookings awaiting an answer"), ("/shipments/in-progress", "Shipments in progress"), ("/shipments/delivered-without-proof-of-delivery", "Delivered without Proof of delivery"), ("/exceptions", "Open Exceptions"));
        GroupLinks(layout, "Own fleet").Should().Equal(("/capacity", "Capacity"), ("/trips/shipments", "Shipments to put on Trips"), ("/trips", "Trips"));
    }

    [Fact]
    public void Render_AsCustomerService_HasNoHomeLinkAndNoMenus()
    {
        var layout = RenderAs("customer-service");

        layout.FindAll("nav[aria-label=Main] details").Should().BeEmpty();
        layout.FindAll("nav[aria-label=Main] a").Select(link => link.TextContent.Trim()).Should().NotContain("Home");
    }

    [Fact]
    public void Render_OnAViewOfAGroup_HighlightsThatGroup()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/exceptions");

        var layout = RenderAs("planner");

        layout.FindAll("nav[aria-label=Main] summary").Select(summary => (summary.TextContent.Trim(), summary.ClassList.Contains("text-white"))).Should()
            .Equal(("Plan", false), ("Follow", true), ("Own fleet", false));
    }

    private static List<(string?, string)> GroupLinks(IRenderedComponent<MainLayout> layout, string group) =>
        [.. layout.FindAll($"nav[aria-label=Main] ul[aria-label='{group}'] a").Select(link => (link.GetAttribute("href"), link.TextContent.Trim()))];

    private IRenderedComponent<MainLayout> RenderAs(string role)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{NorviaRole.CookieName}={role}";
        return Render<MainLayout>(parameters => parameters
            .AddCascadingValue<HttpContext>(context)
            .Add(layout => layout.Body, (RenderFragment)(builder => builder.AddContent(0, "page"))));
    }

    // The brand links home; the rest are the role's views.
    private static List<(string?, string)> MainNavigation(IRenderedComponent<MainLayout> layout) =>
        [.. layout.FindAll("nav[aria-label=Main] a").Skip(1).Select(link => (link.GetAttribute("href"), link.TextContent.Trim()))];
}
