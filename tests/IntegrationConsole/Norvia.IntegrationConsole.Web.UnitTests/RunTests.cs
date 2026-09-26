using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Runs.Open;
using Norvia.IntegrationConsole.Application.Runs.Steps;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Web.Operators;
using Norvia.IntegrationConsole.Web.Runs;

namespace Norvia.IntegrationConsole.Web.UnitTests;

public sealed class RunTests : ConsoleWebContext
{
    private const string Booking = """{"partnerReference":"BD-2026-0042","consignee":{"name":"Café De Kroon"}}""";

    // 12:00 in Rotterdam (CEST).
    private static readonly DateTimeOffset Started = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowRun Run = new("logic-intake-abc123", "transport-order-intake", "08584421797283920101CU00");

    private readonly IRunRequests _runs = Substitute.For<IRunRequests>();

    public RunTests()
    {
        Services.AddSingleton(_runs);
        Answer(Kept(
            Step("manual", "Succeeded", 0, TimeSpan.FromMilliseconds(50)),
            Step("Map_partner_booking", "Succeeded", 0.1, TimeSpan.FromMilliseconds(250)),
            Step("Submit_to_TMS", "Failed", 0.4, TimeSpan.FromSeconds(2.5)) with
            {
                Error = new StepError("ServiceUnavailable", "The TMS answered 503."),
            }));
    }

    [Fact]
    public void Authorize_ForAnyOperator_RequiresTheReadRole()
    {
        // A run's steps lead to its bodies, which hold personal data.
        typeof(Runs.Run).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == OperatorSignIn.ReadPolicy);
    }

    [Fact]
    public void Render_ForARun_OpensIt_AndNamesItsLogicAppAndWorkflow()
    {
        var page = RenderRun();

        _runs.Received(1).OpenAsync(new OpenRunQuery(Run.LogicApp, Run.Workflow, Run.RunId), Arg.Any<CancellationToken>());
        page.Find("h1").TextContent.Should().Contain("Logic App run").And.Contain(Run.RunId);
        page.Markup.Should().Contain("transport-order-intake").And.Contain("logic-intake-abc123");
    }

    [Fact]
    public void Render_ForAKeptRun_ShowsEachStepWithItsStatusStartDurationAndError()
    {
        var page = RenderRun();

        page.Find("[data-run-status]").TextContent.Should().Contain("Failed").And.Contain("3 s");
        var rows = page.FindAll("tbody tr");
        rows.Should().HaveCount(3);
        rows[0].TextContent.Should().Contain("manual").And.Contain("Succeeded").And.Contain("Sat 26 Sep 2026, 12:00:00").And.Contain("50 ms");
        rows[1].TextContent.Should().Contain("Map_partner_booking").And.Contain("250 ms");
        rows[2].TextContent.Should().Contain("Submit_to_TMS").And.Contain("Failed").And.Contain("2.5 s")
            .And.Contain("ServiceUnavailable: The TMS answered 503.");
    }

    [Fact]
    public void Render_ForAKeptRun_ShowsNoBodyUntilAStepIsOpened()
    {
        var page = RenderRun();

        _runs.DidNotReceiveWithAnyArgs().OpenStepAsync(default!, Xunit.TestContext.Current.CancellationToken);
        page.FindAll("pre").Should().BeEmpty();
    }

    [Fact]
    public void Render_ForAStepWithSecureInputsOrOutputs_MarksThemHidden()
    {
        Answer(Kept(
            Step("Get_carrier_credentials", "Succeeded", 0, null) with { OutputsHidden = true },
            Step("Call_carrier", "Succeeded", 1, null) with { InputsHidden = true }));

        var page = RenderRun();

        var rows = page.FindAll("tbody tr");
        rows[0].QuerySelectorAll("[data-hidden]").Select(marker => marker.TextContent.Trim()).Should().Equal("Output hidden");
        rows[1].QuerySelectorAll("[data-hidden]").Select(marker => marker.TextContent.Trim()).Should().Equal("Input hidden");
    }

    [Fact]
    public void Render_ForARunPastItsRetention_SaysTheBodyIsNoLongerKept_AndThatTheHopStaysOnTheTrail()
    {
        Answer(new RunDetail(Run, IsKept: false, null, null, null, [], 90));

        var page = RenderRun();

        page.Find("[role=status]").TextContent.Should().Contain("Body no longer kept (run history is kept for 90 days).")
            .And.Contain("Message trail");
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhileOpening_SaysSo()
    {
        _runs.OpenAsync(Arg.Any<OpenRunQuery>(), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<Result<RunDetail>>().Task);

        var page = RenderRun();

        page.Find("[role=status]").TextContent.Should().Contain("Opening the run");
    }

    [Fact]
    public void Render_ForARunWithoutSteps_SaysSo()
    {
        Answer(Kept());

        var page = RenderRun();

        page.Find("[role=status]").TextContent.Should().Contain("Run history has no steps for this run yet.");
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenTheRunCantBeOpened_SaysSo_WithoutTheReason()
    {
        _runs.OpenAsync(Arg.Any<OpenRunQuery>(), Arg.Any<CancellationToken>())
            .Returns<Result<RunDetail>>(_ => throw new HttpRequestException("403 AuthorizationFailed"));

        var page = RenderRun();

        page.Find("[role=alert]").TextContent.Should().Contain("The run couldn't be opened.");
        page.Markup.Should().NotContain("AuthorizationFailed");
    }

    [Fact]
    public void Render_WhenTheRunIsInvalid_SaysWhy()
    {
        _runs.OpenAsync(Arg.Any<OpenRunQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<RunDetail>.Invalid(new ValidationError("RunId", "Open a run by its Logic App, workflow and run ID.")));

        var page = RenderRun();

        page.Find("[role=alert]").TextContent.Should().Contain("Open a run by its Logic App, workflow and run ID.");
    }

    [Fact]
    public void Render_Always_OffersNoWayToResubmitTheRun()
    {
        // A run is never resubmitted from the console (docs/operations/integration-console.md).
        var page = RenderRun();

        page.FindAll("button, a").Should().NotContain(control => control.TextContent.Contains("Resubmit", StringComparison.OrdinalIgnoreCase));
        page.Find("[role=note]").TextContent.Should().Contain("never resubmits");
    }

    [Fact]
    public void OpeningAStep_FetchesItsBodies_AndShowsThemInFull()
    {
        AnswerStep("Map_partner_booking", StepBody.Shown(Booking), StepBody.Shown("""{"transportOrder":{"number":null}}"""));
        var page = RenderRun();

        page.Find("fluent-button[data-step='Map_partner_booking']").Click();

        _runs.Received(1).OpenStepAsync(new OpenStepQuery(Run.LogicApp, Run.Workflow, Run.RunId, "Map_partner_booking"), Arg.Any<CancellationToken>());
        var step = page.Find("[aria-label='Step Map_partner_booking']");
        var bodies = step.QuerySelectorAll("pre").Select(body => body.TextContent).ToList();
        bodies.Should().HaveCount(2);
        // Indented, and as it was sent: nothing left out, nothing escaped.
        bodies[0].Should().Be("""
            {
              "partnerReference": "BD-2026-0042",
              "consignee": {
                "name": "Café De Kroon"
              }
            }
            """);
        bodies[1].Should().Contain("\"number\": null");
    }

    [Fact]
    public void OpeningAStep_WithABodyThatIsNoJson_ShowsItAsItIs()
    {
        AnswerStep("Map_partner_booking", StepBody.Shown("<booking ref=\"BD-2026-0042\"/>"), StepBody.None);
        var page = RenderRun();

        page.Find("fluent-button[data-step='Map_partner_booking']").Click();

        page.Find("[aria-label='Step Map_partner_booking'] pre").TextContent.Should().Be("<booking ref=\"BD-2026-0042\"/>");
        page.Find("[aria-label='Step Map_partner_booking']").TextContent.Should().Contain("No output recorded.");
    }

    [Fact]
    public void OpeningAStep_WithSecureInputs_SaysTheInputIsHidden()
    {
        AnswerStep("Map_partner_booking", StepBody.Hidden, StepBody.Hidden);
        var page = RenderRun();

        page.Find("fluent-button[data-step='Map_partner_booking']").Click();

        var step = page.Find("[aria-label='Step Map_partner_booking']");
        step.QuerySelectorAll("pre").Should().BeEmpty();
        step.TextContent.Should().Contain("Input hidden: the step uses secure inputs, so run history doesn't show it.")
            .And.Contain("Output hidden: the step uses secure outputs, so run history doesn't show it.");
    }

    [Fact]
    public void OpeningAStep_OfARunPastItsRetention_SaysTheBodyIsNoLongerKept()
    {
        _runs.OpenStepAsync(Arg.Any<OpenStepQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new StepDetail(Run, "Map_partner_booking", IsKept: false, StepBody.None, StepBody.None, 90)));
        var page = RenderRun();

        page.Find("fluent-button[data-step='Map_partner_booking']").Click();

        page.Find("[aria-label='Step Map_partner_booking'] [role=status]").TextContent
            .Should().Contain("Body no longer kept (run history is kept for 90 days).");
    }

    [Fact]
    public void OpeningAStep_WhileFetching_SaysSo()
    {
        _runs.OpenStepAsync(Arg.Any<OpenStepQuery>(), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<Result<StepDetail>>().Task);
        var page = RenderRun();

        page.Find("fluent-button[data-step='Submit_to_TMS']").Click();

        page.Find("[aria-label='Step Submit_to_TMS'] [role=status]").TextContent.Should().Contain("Fetching the bodies of Submit_to_TMS");
    }

    [Fact]
    public void OpeningAStep_WhenItsBodiesCantBeFetched_SaysSo()
    {
        _runs.OpenStepAsync(Arg.Any<OpenStepQuery>(), Arg.Any<CancellationToken>())
            .Returns<Result<StepDetail>>(_ => throw new HttpRequestException("500"));
        var page = RenderRun();

        page.Find("fluent-button[data-step='Submit_to_TMS']").Click();

        page.Find("[aria-label='Step Submit_to_TMS'] [role=alert]").TextContent.Should().Contain("The bodies of Submit_to_TMS couldn't be fetched.");
    }

    [Fact]
    public void OpeningAnotherStep_ShowsThatStepInstead()
    {
        AnswerStep("manual", StepBody.Shown("{}"), StepBody.Shown(Booking));
        AnswerStep("Map_partner_booking", StepBody.Shown(Booking), StepBody.None);
        var page = RenderRun();

        page.Find("fluent-button[data-step='manual']").Click();
        page.Find("fluent-button[data-step='Map_partner_booking']").Click();

        page.FindAll("[aria-label^='Step ']").Select(step => step.GetAttribute("aria-label")).Should().Equal("Step Map_partner_booking");
        page.Find("fluent-button[data-step='Map_partner_booking']").GetAttribute("aria-expanded").Should().Be("true");
        page.Find("fluent-button[data-step='manual']").GetAttribute("aria-expanded").Should().Be("false");
    }

    private IRenderedComponent<Runs.Run> RenderRun() =>
        Render<Runs.Run>(parameters => parameters
            .Add(page => page.LogicApp, Run.LogicApp)
            .Add(page => page.Workflow, Run.Workflow)
            .Add(page => page.RunId, Run.RunId));

    private static RunStep Step(string name, string status, double startSecond, TimeSpan? duration) =>
        new(name, status, Started.AddSeconds(startSecond), duration, null, false, false);

    private static RunDetail Kept(params RunStep[] steps) =>
        new(Run, IsKept: true, steps.Any(step => step.Status == "Failed") ? "Failed" : "Succeeded", Started, TimeSpan.FromSeconds(3), steps, 90);

    private void Answer(RunDetail run) =>
        _runs.OpenAsync(Arg.Any<OpenRunQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(run));

    private void AnswerStep(string step, StepBody input, StepBody output) =>
        _runs.OpenStepAsync(Arg.Is<OpenStepQuery>(query => query.Step == step), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new StepDetail(Run, step, IsKept: true, input, output, 90)));
}
