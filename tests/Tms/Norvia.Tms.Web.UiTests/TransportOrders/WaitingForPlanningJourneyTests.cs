using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.TransportOrders;

/// <summary>The Planner's journey: a Transport Order accepted while the list is open appears in it, and opens in full.</summary>
public sealed class WaitingForPlanningJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    private readonly TmsUiFixture _tms = tms;

    [Fact]
    public async Task AnOrderAcceptedWhileTheListIsOpen_AppearsWithoutARefresh_AndOpensWithEverythingTheShipperSubmitted()
    {
        var shipperReference = $"UI-{Guid.NewGuid():N}"[..20];
        await Page.GotoAsync("/transport-orders");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Transport Orders waiting for planning" })).ToBeVisibleAsync();
        // Submit only once the open list has been read, so the order can only arrive through the live update.
        await Expect(Page.GetByText("Loading the Transport Orders waiting for planning")).ToBeHiddenAsync();

        var number = await SubmitAsync(Bookings.Valid(shipperReference));

        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });
        await Expect(row).ToBeVisibleAsync();
        await Expect(row).ToContainTextAsync("Brouwer Dranken");

        await row.GetByRole(AriaRole.Link, new() { Name = number }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Transport Order {number}", Level = 1 })).ToBeVisibleAsync();
        await Expect(Region("Booking")).ToContainTextAsync(shipperReference);
        await Expect(Region("Collection")).ToContainTextAsync("Waalhaven Oostzijde 88, 3087 BM Rotterdam, NL");
        await Expect(Region("Delivery")).ToContainTextAsync("Café De Zwaan");
        await Expect(Region("Delivery")).ToContainTextAsync("Sanne de Vries");
        await Expect(Region("Delivery")).ToContainTextAsync("+31 30 555 0142");
        var goodsLines = Region("Goods").GetByRole(AriaRole.Row);
        await Expect(goodsLines.Filter(new() { HasText = "pallet" })).ToContainTextAsync("850.5");
        await Expect(goodsLines.Filter(new() { HasText = "crate" })).ToContainTextAsync("60");
        await Expect(Region("Instructions")).ToContainTextAsync("Ring at the side entrance");
    }

    private ILocator Region(string name) => Page.GetByRole(AriaRole.Region, new() { Name = name });

    private async Task<string> SubmitAsync(JsonObject booking)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative))
        {
            Content = JsonContent.Create(booking),
        };
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        using var response = await _tms.Intake.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var accepted = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        return accepted!["transportOrderNumber"]!.GetValue<string>();
    }
}
