using Microsoft.Playwright;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Trails;
using static Microsoft.Playwright.Assertions;

namespace Norvia.IntegrationConsole.Web.UiTests.Runs;

// The failed runs panel lists what IFailedRuns answers for the whole estate, so only the tests of this class set it, and
// the tests of a class run one after the other.
public sealed class FailedRunJourneyTests(ConsoleUiFixture console) : BrowserPage(console)
{
    [Fact]
    public async Task AnOperatorOpeningFailedRuns_SeesEachRunNewestFirst_WithNothingToResubmit()
    {
        var now = DateTimeOffset.UtcNow;
        var older = new WorkflowRun("logic-intake", "transport-order-intake", $"0858{Random.Shared.Next(100_000, 999_999)}CU00");
        var newer = new WorkflowRun("logic-intake", "transport-order-intake", $"0859{Random.Shared.Next(100_000, 999_999)}CU00");
        var correlationId = Guid.NewGuid().ToString();
        Console.Port<IFailedRuns>().FailedRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(
        [
            new RecordedFailedRun(older, now.AddHours(-2), "transport-order-intake", null, "Map_partner_booking", new StepError("InvalidTemplate", "The mapping failed.")),
            new RecordedFailedRun(newer, now.AddMinutes(-5), "transport-order-intake", correlationId, "Submit_to_TMS", new StepError("ServiceUnavailable", "The TMS answered 503.")),
        ]);
        await SignInAsync("Iris Operator", "IntegrationConsole.Read", "IntegrationConsole.Resubmit");

        await Page.GotoAsync("/failed-runs");

        var rows = Page.GetByRole(AriaRole.Table, new() { Name = "Failed runs found" }).GetByRole(AriaRole.Row);
        await Expect(rows.Nth(1)).ToContainTextAsync("Submit_to_TMS");
        await Expect(rows.Nth(1)).ToContainTextAsync("ServiceUnavailable: The TMS answered 503.");
        await Expect(rows.Nth(1).GetByRole(AriaRole.Link, new() { Name = correlationId })).ToBeVisibleAsync();
        await Expect(rows.Nth(2)).ToContainTextAsync("Map_partner_booking");
        await Expect(Page.GetByRole(AriaRole.Note)).ToContainTextAsync("Failed runs are view only.");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Resubmit" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Checkbox)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task TheFailedRunsPanel_ShowsLoadingThenItsRuns()
    {
        var loaded = new TaskCompletionSource<IReadOnlyList<RecordedFailedRun>>();
        var flow = $"flow-{Guid.NewGuid():N}";
        Console.Port<IFailedRuns>().FailedRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => loaded.Task);
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");

        await Page.GotoAsync("/failed-runs");

        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Loading the failed runs…");

        loaded.SetResult([new RecordedFailedRun(null, DateTimeOffset.UtcNow.AddMinutes(-1), flow, null, null, null)]);

        await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Failed runs found" })).ToContainTextAsync(flow);
    }

    [Fact]
    public async Task TheFailedRunsPanel_WithNoFailedRuns_SaysSo()
    {
        Console.Port<IFailedRuns>().FailedRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");

        await Page.GotoAsync("/failed-runs");

        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("No failed runs between");
    }

    [Fact]
    public async Task TheFailedRunsPanel_WhenTheLogicAppsCantBeRead_ShowsAnError()
    {
        Console.Port<IFailedRuns>().FailedRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<RecordedFailedRun>>(_ => throw new InvalidOperationException("The Logic Apps are unreachable."));
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");

        await Page.GotoAsync("/failed-runs");

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("The failed runs couldn't be listed.");
    }
}
