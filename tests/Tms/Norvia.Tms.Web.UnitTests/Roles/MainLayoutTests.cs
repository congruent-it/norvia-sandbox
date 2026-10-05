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
    public void Render_AsCustomerService_LinksToTheRejectedBookingsAndTheRatingExceptions()
    {
        var layout = RenderAs("customer-service");

        MainNavigation(layout).Should().Equal(("/rejected-bookings", "Rejected bookings"), ("/rating-exceptions", "Rating exceptions"));
    }

    [Fact]
    public void Render_AsThePlanner_LinksToWaitingForPlanningShipmentsToReleaseBookingsAwaitingAnAnswerAndShipmentsInProgress()
    {
        var layout = RenderAs("planner");

        MainNavigation(layout).Should().Equal(("/transport-orders", "Waiting for planning"), ("/shipments/to-release", "Shipments to release"), ("/shipments/awaiting-answer", "Bookings awaiting an answer"), ("/shipments/in-progress", "Shipments in progress"));
    }

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
