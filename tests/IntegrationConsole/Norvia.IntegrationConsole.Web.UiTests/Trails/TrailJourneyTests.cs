using Microsoft.Playwright;
using Norvia.IntegrationConsole.Application.Trails;
using static Microsoft.Playwright.Assertions;

namespace Norvia.IntegrationConsole.Web.UiTests.Trails;

public sealed class TrailJourneyTests(ConsoleUiFixture console) : BrowserPage(console)
{
    [Fact]
    public async Task AnOperatorOpeningATrailFromSearch_SeesItsHopsInOrder_AndFollowsABusinessKeyToASearch()
    {
        var number = $"TO-2026-{Random.Shared.Next(100_000, 999_999)}";
        var correlationId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        BusinessKey[] keys = [new(BusinessKeyKind.TransportOrderNumber, number)];
        Console.Port<ITrailSource>().SearchAsync(Arg.Is<TrailSearch>(search => search.Key == number), Arg.Any<CancellationToken>()).Returns(
        [
            new FoundTrail(correlationId, ["transport-order-intake"], now.AddMinutes(-3), now.AddMinutes(-2), [Outcome.Succeeded], keys),
        ]);
        Console.Port<ITrailSource>().HopsAsync(Arg.Is<TrailLookup>(lookup => lookup.CorrelationId == correlationId), Arg.Any<CancellationToken>()).Returns(
        [
            new RecordedHop("tms", "run", "tms-api: POST /v1/transport-orders", "transport-order-intake", now.AddMinutes(-2.5),
                TimeSpan.FromMilliseconds(300), Outcome.Succeeded, null, keys),
            new RecordedHop("apim", null, "apim-norvia: Submit transport order", "transport-order-intake", now.AddMinutes(-3),
                TimeSpan.FromSeconds(2), Outcome.Succeeded, null, []),
            new RecordedHop("run", "apim", "logic-intake: transport-order-intake", "transport-order-intake", now.AddMinutes(-2.9),
                TimeSpan.FromSeconds(1.5), Outcome.Succeeded, null, keys, new WorkflowRun("logic-intake", "transport-order-intake", "0858")),
        ]);
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");
        await Page.GotoAsync($"/?key={number}");

        await Page.GetByRole(AriaRole.Link, new() { Name = correlationId }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/trails/{correlationId}$"));
        await Expect(Page.GetByRole(AriaRole.Note)).ToContainTextAsync("Telemetry may be a few minutes behind");
        var hops = Page.GetByRole(AriaRole.Table, new() { Name = "Hops" }).GetByRole(AriaRole.Row).Filter(new() { HasText = "transport-order-intake" });
        await Expect(hops).ToHaveCountAsync(3);
        await Expect(hops.Nth(0)).ToContainTextAsync("apim-norvia: Submit transport order");
        await Expect(hops.Nth(1)).ToContainTextAsync("logic-intake: transport-order-intake");
        await Expect(hops.Nth(1).GetByRole(AriaRole.Link, new() { Name = "Open run" })).ToHaveAttributeAsync("href", "/runs/logic-intake/transport-order-intake/0858");
        await Expect(hops.Nth(2)).ToContainTextAsync("tms-api: POST /v1/transport-orders");
        (await CutOffColumnTitlesAsync(Page.GetByRole(AriaRole.Table, new() { Name = "Hops" }))).Should().BeEmpty();

        await Page.GetByRole(AriaRole.Region, new() { Name = "Business keys" }).GetByRole(AriaRole.Link, new() { Name = $"Transport Order {number}" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"\\?key={number}&within=all$"));
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync($"1 Message trail carried {number}");
    }

    [Fact]
    public async Task AnOperatorOpeningATrailWhenTheTelemetryCantBeReached_IsToldItCouldntBeOpened()
    {
        var correlationId = Guid.NewGuid().ToString();
        Console.Port<ITrailSource>().HopsAsync(Arg.Is<TrailLookup>(lookup => lookup.CorrelationId == correlationId), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<RecordedHop>>(_ => throw new InvalidOperationException("Log Analytics is down."));
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");

        await Page.GotoAsync($"/trails/{correlationId}");

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("The Message trail couldn't be opened.");
    }
}
