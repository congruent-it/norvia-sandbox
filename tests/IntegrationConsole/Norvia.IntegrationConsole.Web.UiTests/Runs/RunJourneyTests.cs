using Microsoft.Playwright;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Trails;
using static Microsoft.Playwright.Assertions;

namespace Norvia.IntegrationConsole.Web.UiTests.Runs;

public sealed class RunJourneyTests(ConsoleUiFixture console) : BrowserPage(console)
{
    [Fact]
    public async Task AnOperatorOpeningARunFromItsTrail_SeesItsSteps_AndOpensAStepToItsBodies()
    {
        var correlationId = Guid.NewGuid().ToString();
        var run = new WorkflowRun("logic-intake", "transport-order-intake", $"0858{Random.Shared.Next(100_000, 999_999)}CU00");
        var now = DateTimeOffset.UtcNow;
        Console.Port<ITrailSource>().HopsAsync(Arg.Is<TrailLookup>(lookup => lookup.CorrelationId == correlationId), Arg.Any<CancellationToken>()).Returns(
        [
            new RecordedHop("run", null, "logic-intake: transport-order-intake", "transport-order-intake", now.AddMinutes(-3),
                TimeSpan.FromSeconds(3), Outcome.Failed, "TmsUnavailable", [], run),
        ]);
        Console.Port<IWorkflowRuns>().RunAsync(run, Arg.Any<CancellationToken>()).Returns(new RecordedRun("Failed", now.AddMinutes(-3), now.AddMinutes(-3).AddSeconds(3),
        [
            new RecordedStep("manual", "Succeeded", now.AddMinutes(-3), now.AddMinutes(-3).AddMilliseconds(50), null, false, false),
            new RecordedStep("Map_partner_booking", "Succeeded", now.AddMinutes(-3).AddMilliseconds(100), now.AddMinutes(-3).AddMilliseconds(350), null, false, false),
            new RecordedStep("Submit_to_TMS", "Failed", now.AddMinutes(-3).AddMilliseconds(400), now.AddMinutes(-3).AddSeconds(2.9),
                new StepError("ServiceUnavailable", "The TMS answered 503."), false, false),
        ]));
        Console.Port<IWorkflowRuns>().BodiesAsync(run, "Map_partner_booking", Arg.Any<CancellationToken>()).Returns(new RecordedBodies(
            new RecordedBody(false, """{"partnerReference":"BD-2026-0042"}"""),
            new RecordedBody(false, """{"transportOrder":{"consignee":{"name":"Café De Kroon"}}}""")));
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");
        await Page.GotoAsync($"/trails/{correlationId}");

        await Page.GetByRole(AriaRole.Link, new() { Name = "Open run" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync(run.RunId);
        var steps = Page.GetByRole(AriaRole.Table, new() { Name = "Steps" });
        await Expect(steps.GetByRole(AriaRole.Row).Filter(new() { HasText = "Submit_to_TMS" })).ToContainTextAsync("ServiceUnavailable: The TMS answered 503.");
        await Expect(Page.GetByText("Café De Kroon")).ToHaveCountAsync(0);
        (await CutOffColumnTitlesAsync(steps)).Should().BeEmpty();

        await steps.GetByRole(AriaRole.Button, new() { Name = "Map_partner_booking" }).ClickAsync();

        var step = Page.GetByRole(AriaRole.Region, new() { Name = "Step Map_partner_booking" });
        await Expect(step).ToContainTextAsync("\"partnerReference\": \"BD-2026-0042\"");
        await Expect(step).ToContainTextAsync("\"name\": \"Café De Kroon\"");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Resubmit" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AnOperatorOpeningARunPastItsRetention_IsToldItsBodyIsNoLongerKept()
    {
        var run = new WorkflowRun("logic-intake", "transport-order-intake", $"0859{Random.Shared.Next(100_000, 999_999)}CU00");
        Console.Port<IWorkflowRuns>().RunAsync(run, Arg.Any<CancellationToken>()).Returns((RecordedRun?)null);
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");

        await Page.GotoAsync($"/runs/{run.LogicApp}/{run.Workflow}/{run.RunId}");

        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Body no longer kept (run history is kept for 90 days).");
    }

    [Fact]
    public async Task AnOperatorWithoutTheReadRole_IsRefusedARun()
    {
        // Every run page leads to bodies, which only Read allows.
        var run = new WorkflowRun("logic-intake", "transport-order-intake", $"0857{Random.Shared.Next(100_000, 999_999)}CU00");
        await SignInAsync("Sam Stranger", "IntegrationConsole.Resubmit");

        var response = await Page.GotoAsync($"/runs/{run.LogicApp}/{run.Workflow}/{run.RunId}");

        response!.Status.Should().Be(403);
        await Console.Port<IWorkflowRuns>().DidNotReceive().RunAsync(run, Arg.Any<CancellationToken>());
    }
}
