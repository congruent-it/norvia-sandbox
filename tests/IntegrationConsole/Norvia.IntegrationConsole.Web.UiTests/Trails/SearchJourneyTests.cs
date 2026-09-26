using Microsoft.Playwright;
using Norvia.IntegrationConsole.Application.Trails;
using static Microsoft.Playwright.Assertions;

namespace Norvia.IntegrationConsole.Web.UiTests.Trails;

public sealed class SearchJourneyTests(ConsoleUiFixture console) : BrowserPage(console)
{
    [Fact]
    public async Task AnOperatorSearchingATransportOrderNumber_SeesEveryTrailThatCarriedIt_FailedOnesFirst_WithKeysOnly()
    {
        var number = $"TO-2026-{Random.Shared.Next(100_000, 999_999)}";
        var now = DateTimeOffset.UtcNow;
        Console.Port<ITrailSource>().SearchAsync(Arg.Is<TrailSearch>(search => search.Key == number), Arg.Any<CancellationToken>()).Returns(
        [
            new FoundTrail("corr-booked", ["transport-order-intake"], now.AddMinutes(-3), now.AddMinutes(-2), [Outcome.Succeeded],
                [new(BusinessKeyKind.TransportOrderNumber, number)]),
            new FoundTrail("corr-failed", ["transport-order-intake"], now.AddHours(-3), now.AddHours(-3), [Outcome.Succeeded, Outcome.Failed],
                [new(BusinessKeyKind.TransportOrderNumber, number)]),
        ]);
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");
        await Page.GotoAsync("/");

        await Page.GetByLabel("Correlation ID or business key").FillAsync(number);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();

        var results = Page.GetByRole(AriaRole.Table, new() { Name = "Message trails found" });
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"\\?key={number}&within=24h$"));
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync($"2 Message trails carried {number}");
        await Expect(results.GetByRole(AriaRole.Columnheader)).ToHaveTextAsync(
            ["Correlation ID", "Flows", "First seen", "Last seen", "Outcome", "Business keys"]);
        (await CutOffColumnTitlesAsync(results)).Should().BeEmpty();
        var rows = results.GetByRole(AriaRole.Row).Filter(new() { HasText = "corr-" });
        await Expect(rows).ToHaveCountAsync(2);
        await Expect(rows.Nth(0)).ToContainTextAsync("corr-failed");
        await Expect(rows.Nth(0)).ToContainTextAsync("Failed");
        await Expect(rows.Nth(1)).ToContainTextAsync("corr-booked");
        await Expect(rows.Nth(1)).ToContainTextAsync($"Transport Order {number}");
    }

    [Fact]
    public async Task AnOperatorSearchingWhenTheTelemetryCantBeReached_IsToldTheSearchFailed()
    {
        const string key = "corr-while-log-analytics-is-down";
        Console.Port<ITrailSource>().SearchAsync(Arg.Is<TrailSearch>(search => search.Key == key), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<FoundTrail>>(_ => throw new InvalidOperationException("Log Analytics is down."));
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");

        await Page.GotoAsync($"/?key={key}");

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("The search couldn't be run.");
    }
}
