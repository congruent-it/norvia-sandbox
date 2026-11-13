using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.IntegrationConsole.Application.Refusals;
using Norvia.IntegrationConsole.Application.Trails;
using static Microsoft.Playwright.Assertions;

namespace Norvia.IntegrationConsole.Web.UiTests.Refusals;

// The panel lists what IRefusedCalls answers for the whole estate, so only the tests of this class set it, and the tests
// of a class run one after the other. Every test names its own operations and callers, so none depends on another's data.
public sealed class RefusedAtTheEdgeJourneyTests(ConsoleUiFixture console) : BrowserPage(console)
{
    private const string Operator = "Iris Operator";

    [Fact]
    public async Task AnOperatorOpeningTheRefusedCallsPanel_SeesACarriersRefusedCall_AndOpensItsTrail()
    {
        var operation = OperationName();
        var carrier = CallerName();
        var correlationId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        RefusalsAre(new RecordedRefusal(now.AddMinutes(-2), operation, RefusalStatus.Forbidden, "MissingRole", carrier, correlationId));
        Console.Port<ITrailSource>().HopsAsync(Arg.Is<TrailLookup>(lookup => lookup.CorrelationId == correlationId), Arg.Any<CancellationToken>()).Returns(
        [
            new RecordedHop("apim", null, $"apim-norvia: {operation}", operation, now.AddMinutes(-2), TimeSpan.FromMilliseconds(40), Outcome.Refused, "MissingRole", []),
        ]);
        await SignInAsync(Operator, "IntegrationConsole.Read");

        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Refused at the edge" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Refused at the edge" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Note)).ToContainTextAsync("A refusal is the answer the caller was meant to get.");
        var row = CallsTable.GetByRole(AriaRole.Row).Filter(new() { HasText = operation });
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row).ToContainTextAsync("403");
        await Expect(row).ToContainTextAsync("MissingRole");
        await Expect(row).ToContainTextAsync(carrier);

        await row.GetByRole(AriaRole.Link, new() { Name = correlationId }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"/trails/{correlationId}$"));
        await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Hops" })).ToContainTextAsync($"apim-norvia: {operation}");
    }

    [Fact]
    public async Task AnOperatorFilteringByStatusAndOperation_SeesOnlyTheMatchingCalls_AndTheSummaryGroupsThemByCaller()
    {
        var submit = OperationName();
        var track = OperationName();
        var expired = CallerName();
        var throttled = CallerName();
        var now = DateTimeOffset.UtcNow;
        RefusalsAre(
            new RecordedRefusal(now.AddMinutes(-9), submit, RefusalStatus.Unauthorized, "TokenExpired", expired, Guid.NewGuid().ToString()),
            new RecordedRefusal(now.AddMinutes(-8), submit, RefusalStatus.Unauthorized, "TokenExpired", expired, Guid.NewGuid().ToString()),
            new RecordedRefusal(now.AddMinutes(-7), submit, RefusalStatus.Forbidden, "MissingRole", throttled, Guid.NewGuid().ToString()),
            new RecordedRefusal(now.AddMinutes(-6), track, RefusalStatus.TooManyRequests, "RateLimitExceeded", throttled, null));
        await SignInAsync(Operator, "IntegrationConsole.Read");

        await Page.GotoAsync("/refused-at-the-edge");

        var summary = Page.GetByRole(AriaRole.Table, new() { Name = "Refusals by caller" }).GetByRole(AriaRole.Row);
        await Expect(summary.Filter(new() { HasText = expired }).GetByRole(AriaRole.Gridcell)).ToHaveTextAsync([expired, "401", "2"]);
        await Expect(summary.Filter(new() { HasText = throttled })).ToHaveCountAsync(2);
        await Expect(CallsTable.GetByRole(AriaRole.Row).Filter(new() { HasText = submit })).ToHaveCountAsync(3);
        await Expect(CallsTable.GetByRole(AriaRole.Row).Filter(new() { HasText = track })).ToHaveCountAsync(1);

        await ChooseAsync("Status", "401");

        await Expect(Page).ToHaveURLAsync(new Regex("[?&]status=401"));
        await Expect(CallsTable.GetByRole(AriaRole.Row).Filter(new() { HasText = submit })).ToHaveCountAsync(2);
        await Expect(CallsTable.GetByRole(AriaRole.Row).Filter(new() { HasText = track })).ToHaveCountAsync(0);
        await Expect(summary.Filter(new() { HasText = throttled })).ToHaveCountAsync(0);

        await ChooseAsync("Status", "All statuses");
        await ChooseAsync("Operation", track);

        await Expect(Page).ToHaveURLAsync(new Regex($"[?&]operation={Uri.EscapeDataString(track)}"));
        await Expect(CallsTable.GetByRole(AriaRole.Row).Filter(new() { HasText = track })).ToHaveCountAsync(1);
        await Expect(CallsTable.GetByRole(AriaRole.Row).Filter(new() { HasText = submit })).ToHaveCountAsync(0);
        await Expect(summary.Filter(new() { HasText = throttled })).ToContainTextAsync("429");
        await Expect(summary.Filter(new() { HasText = expired })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AUserWithoutTheReadRole_CantOpenTheRefusedCallsPanel()
    {
        var operation = OperationName();
        RefusalsAre(new RecordedRefusal(DateTimeOffset.UtcNow.AddMinutes(-1), operation, RefusalStatus.Forbidden, "MissingRole", CallerName(), null));
        await SignInAsync("Sam Stranger", "IntegrationConsole.Resubmit");

        var response = await Page.GotoAsync("/refused-at-the-edge");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You can't use the Integration console" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Refused at the edge" })).ToHaveCountAsync(0);
        await Expect(Page.GetByText(operation)).ToHaveCountAsync(0);
        response!.Status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    // The Fluent select is a custom element, not a <select>: open it, then pick the option as a person would.
    private async Task ChooseAsync(string label, string option)
    {
        await Page.GetByRole(AriaRole.Combobox, new() { Name = label }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = option, Exact = true }).ClickAsync();
    }

    private ILocator CallsTable => Page.GetByRole(AriaRole.Table, new() { Name = "Refused calls found" });

    private static string OperationName() => $"Operation {Guid.NewGuid():N}";

    private static string CallerName() => $"caller-{Guid.NewGuid():N}";

    private void RefusalsAre(params RecordedRefusal[] refusals) =>
        Console.Port<IRefusedCalls>().RefusalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(refusals);
}
