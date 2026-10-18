using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Planner;

/// <summary>
/// The Planner's home page and grouped navigation (#521): the TMS opens on the work to act on, each item opens its list, the header
/// groups the views into menus, and Customer service still opens on its first view with its own navigation.
/// </summary>
public sealed class PlannerHomeJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    private static readonly (string Title, string Path)[] Items =
    [
        ("Waiting for planning", "/transport-orders"),
        ("Shipments to release", "/shipments/to-release"),
        ("Bookings awaiting an answer", "/shipments/awaiting-answer"),
        ("Open Exceptions", "/exceptions"),
        ("Delivered without Proof of delivery", "/shipments/delivered-without-proof-of-delivery"),
        ("Shipments to put on Trips", "/trips/shipments"),
    ];

    [Fact]
    public async Task ThePlanner_OpensTheTms_OnTheHomePage_WithACountAndALinkForEachItemThatNeedsAction()
    {
        await SubmitAcceptedAsync(Bookings.Valid($"UI-{Guid.NewGuid():N}"[..20]));

        await Page.GotoAsync("/");

        await Expect(Page).ToHaveURLAsync(new Regex("/home$"));
        await Expect(Heading("Home")).ToBeVisibleAsync();
        var work = Page.GetByRole(AriaRole.List, new() { Name = "Work to act on" });
        await Expect(work.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(Items.Length);
        foreach (var (title, path) in Items)
        {
            await Expect(Item(title).GetByRole(AriaRole.Link, new() { Name = title, Exact = true })).ToHaveAttributeAsync("href", path);
        }

        // The order just submitted waits for planning, so that item shows a count rather than saying there's nothing to do.
        await Expect(Item("Waiting for planning").Locator("strong")).ToHaveAttributeAsync("aria-label", new Regex(@"^[1-9]\d* Waiting for planning$"));
    }

    [Fact]
    public async Task AnItemWithNothingToDo_SaysSo_InsteadOfShowingACountOfZero()
    {
        await Page.GotoAsync("/home");

        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Work to act on" })).ToBeVisibleAsync();
        foreach (var (title, _) in Items)
        {
            var item = Item(title);
            // Either work is waiting, with its count, or the item says there is none: never a count of zero.
            if (await item.Locator("strong").CountAsync() == 0)
            {
                await Expect(item.Locator("p")).Not.ToHaveTextAsync("");
            }
            else
            {
                await Expect(item.Locator("strong")).Not.ToHaveTextAsync("0");
            }
        }
    }

    [Fact]
    public async Task FollowingAnItemsLink_OpensItsList()
    {
        foreach (var (title, path) in Items)
        {
            await Page.GotoAsync("/home");
            await Item(title).GetByRole(AriaRole.Link, new() { Name = title, Exact = true }).ClickAsync();

            await Expect(Page).ToHaveURLAsync(new Regex(Regex.Escape(path) + "$"));
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
        }

        await Page.GotoAsync("/home");
        await Item("Waiting for planning").GetByRole(AriaRole.Link).ClickAsync();
        await Expect(Heading("Transport Orders waiting for planning")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheHeader_ShowsGroupedMenusAndAHomeEntry_WithEachViewReachableFromItsGroup_AndTheActiveViewHighlighted()
    {
        await Page.GotoAsync("/home");
        var navigation = Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" });

        var home = navigation.GetByRole(AriaRole.Link, new() { Name = "Home", Exact = true });
        await Expect(home).ToHaveClassAsync(Active);
        foreach (var group in new[] { "Plan", "Follow", "Own fleet" })
        {
            await Expect(navigation.Locator("summary", new() { HasText = group })).ToBeVisibleAsync();
        }

        var views = new (string Group, string View, string Path)[]
        {
            ("Plan", "Waiting for planning", "/transport-orders"),
            ("Plan", "Transport Orders", "/transport-orders/all"),
            ("Plan", "Shipments to release", "/shipments/to-release"),
            ("Follow", "Bookings awaiting an answer", "/shipments/awaiting-answer"),
            ("Follow", "Shipments in progress", "/shipments/in-progress"),
            ("Follow", "Delivered without Proof of delivery", "/shipments/delivered-without-proof-of-delivery"),
            ("Follow", "Open Exceptions", "/exceptions"),
            ("Own fleet", "Capacity", "/capacity"),
            ("Own fleet", "Shipments to put on Trips", "/trips/shipments"),
            ("Own fleet", "Trips", "/trips"),
        };
        foreach (var (group, view, path) in views)
        {
            var menu = navigation.Locator("details").Filter(new() { Has = Page.Locator("summary", new() { HasText = group }) });
            await OpenMenuAsync(menu);
            var link = menu.GetByRole(AriaRole.Link, new() { Name = view, Exact = true });
            await Expect(link).ToBeVisibleAsync();
            await link.ClickAsync();

            await Expect(Page).ToHaveURLAsync(new Regex(Regex.Escape(path) + "$"));
            await Expect(home).Not.ToHaveClassAsync(Active);
            // The view being open is highlighted in its group's menu.
            await OpenMenuAsync(menu);
            await Expect(menu.GetByRole(AriaRole.Link, new() { Name = view, Exact = true })).ToHaveClassAsync(Active);
        }

        await home.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/home$"));
        await Expect(Heading("Home")).ToBeVisibleAsync();
        await Expect(home).ToHaveClassAsync(Active);
    }

    [Fact]
    public async Task CustomerService_StillOpensOnItsFirstView_WithItsOwnNavigation_AndNoHomeOrMenus()
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Customer service" }).ClickAsync();

        await Expect(Heading("Rejected bookings")).ToBeVisibleAsync();
        var navigation = Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" });
        foreach (var view in new[] { "Rejected bookings", "Transport Orders", "Rating exceptions", "Not yet billable" })
        {
            await Expect(navigation.GetByRole(AriaRole.Link, new() { Name = view, Exact = true })).ToBeVisibleAsync();
        }

        await Expect(navigation.GetByRole(AriaRole.Link, new() { Name = "Home", Exact = true })).ToHaveCountAsync(0);
        await Expect(navigation.Locator("details")).ToHaveCountAsync(0);

        // The home page is the Planner's: Customer service's own opening is unchanged when the TMS is opened again.
        await Page.GotoAsync("/");
        await Expect(Heading("Rejected bookings")).ToBeVisibleAsync();
    }

    // A menu is a details element; the page may already have it open, and clicking its summary would close it.
    private static async Task OpenMenuAsync(ILocator menu)
    {
        if (!await menu.EvaluateAsync<bool>("menu => menu.open"))
        {
            await menu.Locator("summary").ClickAsync();
        }
    }

    private static readonly Regex Active = new("!text-white");

    private ILocator Heading(string name) => Page.GetByRole(AriaRole.Heading, new() { Name = name, Level = 1 });

    private ILocator Item(string title) =>
        Page.GetByRole(AriaRole.List, new() { Name = "Work to act on" }).GetByRole(AriaRole.Listitem).Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = title, Exact = true }) });
}
