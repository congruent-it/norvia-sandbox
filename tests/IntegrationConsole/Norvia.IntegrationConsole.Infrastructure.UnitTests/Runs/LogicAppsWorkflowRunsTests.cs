using System.Net;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Infrastructure.Runs;
using static Norvia.IntegrationConsole.Infrastructure.UnitTests.Runs.RunHistory;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.Runs;

/// <summary>
/// The console reads a run as it is now from the Logic Apps Standard management API, as its own identity (ADR 0017), and
/// fetches a step's bodies from run history only when the operator opens the step.
/// </summary>
public sealed class LogicAppsWorkflowRunsTests : IDisposable
{
    private const string Booking = """{"headers":{"Content-Type":"application/json"},"body":{"partnerReference":"BD-2026-0042"}}""";
    private const string TransportOrder = """{"body":{"transportOrder":{"consignee":{"name":"Café De Kroon"}}}}""";

    private static readonly DateTimeOffset Started = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowRun Run = new(LogicApp, Workflow, RunId);

    private readonly RunHistory _history = new();
    private readonly HttpClient _http;
    private readonly TokenCredential _credential = Substitute.For<TokenCredential>();

    public LogicAppsWorkflowRunsTests()
    {
        _http = new HttpClient(_history);
        _credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(new AccessToken("console-token", DateTimeOffset.MaxValue));
        _history.Definition(new JsonObject { ["manual"] = Step() }, new JsonObject { ["Map_partner_booking"] = Step() });
    }

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task RunAsync_ForAKeptRun_ReadsItsTriggerAndActions_WithStatusTimesAndError()
    {
        _history
            .Run("Failed", "2026-09-26T10:00:00Z", "2026-09-26T10:00:03Z", Trigger("manual", "Succeeded", 0, 0.05))
            .Answer(ActionsAddress, Actions(
                Action("Map_partner_booking", "Succeeded", 0.1, 0.35),
                Action("Submit_to_TMS", "Failed", 0.4, 2.9, error: ("ServiceUnavailable", "The TMS answered 503.")),
                Action("Relay_outcome", "Skipped", null, null)));

        var run = await WorkflowRuns().RunAsync(Run, TestContext.Current.CancellationToken);

        run.Should().BeEquivalentTo(new RecordedRun("Failed", Started, Started.AddSeconds(3),
        [
            new RecordedStep("manual", "Succeeded", Started, Started.AddSeconds(0.05), null, false, false),
            new RecordedStep("Map_partner_booking", "Succeeded", Started.AddSeconds(0.1), Started.AddSeconds(0.35), null, false, false),
            new RecordedStep("Submit_to_TMS", "Failed", Started.AddSeconds(0.4), Started.AddSeconds(2.9),
                new StepError("ServiceUnavailable", "The TMS answered 503."), false, false),
            new RecordedStep("Relay_outcome", "Skipped", null, null, null, false, false),
        ]));
    }

    [Fact]
    public async Task RunAsync_CallsTheManagementApi_AsTheConsolesOwnIdentity()
    {
        _history.Run("Succeeded", "2026-09-26T10:00:00Z", "2026-09-26T10:00:01Z", Trigger("manual", "Succeeded", 0, 0.05))
            .Answer(ActionsAddress, Actions());

        await WorkflowRuns().RunAsync(Run, TestContext.Current.CancellationToken);

        _history.Requests.Should().NotBeEmpty().And.OnlyContain(request =>
            request.Headers.Authorization!.Scheme == "Bearer" && request.Headers.Authorization.Parameter == "console-token");
        await _credential.Received().GetTokenAsync(
            Arg.Is<TokenRequestContext>(context => context.Scopes.SequenceEqual(new[] { "https://management.azure.com/.default" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ForManyActions_FollowsEveryPage()
    {
        var nextPage = ActionsAddress + "&$skiptoken=page2";
        _history.Run("Succeeded", "2026-09-26T10:00:00Z", "2026-09-26T10:00:01Z", Trigger("manual", "Succeeded", 0, 0.05))
            .Answer(ActionsAddress, new JsonObject { ["value"] = new JsonArray(Action("First", "Succeeded", 0.1, 0.2)), ["nextLink"] = nextPage })
            .Answer(nextPage, Actions(Action("Second", "Succeeded", 0.3, 0.4)));

        var run = await WorkflowRuns().RunAsync(Run, TestContext.Current.CancellationToken);

        run!.Steps.Select(step => step.Name).Should().Equal("manual", "First", "Second");
    }

    [Fact]
    public async Task RunAsync_ForARunRunHistoryNoLongerKeeps_ReturnsNull()
    {
        var run = await WorkflowRuns().RunAsync(Run, TestContext.Current.CancellationToken);

        run.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_WhenTheManagementApiFails_Throws()
    {
        _history.Answer(RunAddress, HttpStatusCode.Forbidden, """{"error":{"code":"AuthorizationFailed"}}""");

        var read = () => WorkflowRuns().RunAsync(Run, TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task RunAsync_ForStepsWithSecureInputsOrOutputs_SaysSo_FromTheWorkflowsDefinition()
    {
        // Secure data can be set on the trigger and on actions nested in scopes, conditions and loops.
        _history
            .Definition(
                new JsonObject { ["manual"] = Step("inputs") },
                new JsonObject
                {
                    ["Scope"] = new JsonObject
                    {
                        ["type"] = "Scope",
                        ["actions"] = new JsonObject { ["Get_carrier_credentials"] = Step("outputs") },
                    },
                    ["Call_carrier"] = Step("inputs", "outputs"),
                })
            .Run("Succeeded", "2026-09-26T10:00:00Z", "2026-09-26T10:00:01Z", Trigger("manual", "Succeeded", 0, 0.05))
            .Answer(ActionsAddress, Actions(
                Action("Scope", "Succeeded", 0.1, 0.3),
                Action("Get_carrier_credentials", "Succeeded", 0.1, 0.2),
                Action("Call_carrier", "Succeeded", 0.3, 0.5)));

        var run = await WorkflowRuns().RunAsync(Run, TestContext.Current.CancellationToken);

        run!.Steps.Select(step => (step.Name, step.SecureInputs, step.SecureOutputs)).Should().Equal(
            ("manual", true, false),
            ("Scope", false, false),
            ("Get_carrier_credentials", false, true),
            ("Call_carrier", true, true));
    }

    [Fact]
    public async Task BodiesAsync_ForAnAction_FetchesItsInputAndOutputBodiesInFull_WithoutTheConsolesToken()
    {
        // The content links are signed; the console's token has no business going to the Logic App's host.
        var input = ContentLink("Map_partner_booking", "ActionInputs");
        var output = ContentLink("Map_partner_booking", "ActionOutputs");
        _history
            .Run("Succeeded", "2026-09-26T10:00:00Z", "2026-09-26T10:00:01Z", Trigger("manual", "Succeeded", 0, 0.05))
            .Answer(ActionAddress("Map_partner_booking"), Action("Map_partner_booking", "Succeeded", 0.1, 0.35, input, output))
            .Answer(input, HttpStatusCode.OK, Booking)
            .Answer(output, HttpStatusCode.OK, TransportOrder);

        var bodies = await WorkflowRuns().BodiesAsync(Run, "Map_partner_booking", TestContext.Current.CancellationToken);

        bodies.Should().Be(new RecordedBodies(new RecordedBody(false, Booking), new RecordedBody(false, TransportOrder)));
        _history.Requests.Where(request => request.RequestUri!.Host != "management.azure.com")
            .Should().HaveCount(2).And.OnlyContain(request => request.Headers.Authorization == null);
    }

    [Fact]
    public async Task BodiesAsync_ForTheTrigger_FetchesTheTriggersBodies()
    {
        var input = ContentLink("manual", "TriggerInputs");
        var output = ContentLink("manual", "TriggerOutputs");
        _history
            .Run("Succeeded", "2026-09-26T10:00:00Z", "2026-09-26T10:00:01Z", Trigger("manual", "Succeeded", 0, 0.05, input, output))
            .Answer(input, HttpStatusCode.OK, "{}")
            .Answer(output, HttpStatusCode.OK, Booking);

        var bodies = await WorkflowRuns().BodiesAsync(Run, "manual", TestContext.Current.CancellationToken);

        bodies.Should().Be(new RecordedBodies(new RecordedBody(false, "{}"), new RecordedBody(false, Booking)));
    }

    [Fact]
    public async Task BodiesAsync_ForAStepWithSecureInputs_NeverFetchesThem()
    {
        var input = ContentLink("Call_carrier", "ActionInputs");
        var output = ContentLink("Call_carrier", "ActionOutputs");
        _history
            .Definition(new JsonObject { ["manual"] = Step() }, new JsonObject { ["Call_carrier"] = Step("inputs") })
            .Run("Succeeded", "2026-09-26T10:00:00Z", "2026-09-26T10:00:01Z", Trigger("manual", "Succeeded", 0, 0.05))
            .Answer(ActionAddress("Call_carrier"), Action("Call_carrier", "Succeeded", 0.1, 0.35, input, output))
            .Answer(input, HttpStatusCode.OK, """{"password":"hunter2"}""")
            .Answer(output, HttpStatusCode.OK, TransportOrder);

        var bodies = await WorkflowRuns().BodiesAsync(Run, "Call_carrier", TestContext.Current.CancellationToken);

        bodies.Should().Be(new RecordedBodies(RecordedBody.Hidden, new RecordedBody(false, TransportOrder)));
        _history.Requests.Should().NotContain(request => request.RequestUri!.AbsoluteUri == new Uri(input).AbsoluteUri);
    }

    [Fact]
    public async Task BodiesAsync_ForAStepWithoutBodies_SaysThereAreNone()
    {
        _history
            .Run("Succeeded", "2026-09-26T10:00:00Z", "2026-09-26T10:00:01Z", Trigger("manual", "Succeeded", 0, 0.05))
            .Answer(ActionAddress("Relay_outcome"), Action("Relay_outcome", "Skipped", null, null));

        var bodies = await WorkflowRuns().BodiesAsync(Run, "Relay_outcome", TestContext.Current.CancellationToken);

        bodies.Should().Be(new RecordedBodies(RecordedBody.None, RecordedBody.None));
    }

    [Fact]
    public async Task BodiesAsync_ForARunRunHistoryNoLongerKeeps_ReturnsNull()
    {
        var bodies = await WorkflowRuns().BodiesAsync(Run, "Map_partner_booking", TestContext.Current.CancellationToken);

        bodies.Should().BeNull();
    }

    [Fact]
    public async Task BodiesAsync_ForAStepTheRunDoesntHave_Throws()
    {
        // The run is kept, so a step it doesn't have is a wrong address, not an expired one.
        _history.Run("Succeeded", "2026-09-26T10:00:00Z", "2026-09-26T10:00:01Z", Trigger("manual", "Succeeded", 0, 0.05));

        var fetch = () => WorkflowRuns().BodiesAsync(Run, "No_such_step", TestContext.Current.CancellationToken);

        await fetch.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task BodiesAsync_ForABodyRunHistoryNoLongerHas_SaysThatOneIsNone_AndKeepsTheOther()
    {
        var input = ContentLink("Map_partner_booking", "ActionInputs");
        var output = ContentLink("Map_partner_booking", "ActionOutputs");
        _history
            .Run("Succeeded", "2026-09-26T10:00:00Z", "2026-09-26T10:00:01Z", Trigger("manual", "Succeeded", 0, 0.05))
            .Answer(ActionAddress("Map_partner_booking"), Action("Map_partner_booking", "Succeeded", 0.1, 0.35, input, output))
            .Answer(output, HttpStatusCode.OK, TransportOrder);

        var bodies = await WorkflowRuns().BodiesAsync(Run, "Map_partner_booking", TestContext.Current.CancellationToken);

        bodies.Should().Be(new RecordedBodies(RecordedBody.None, new RecordedBody(false, TransportOrder)));
    }

    [Fact]
    public async Task RunAsync_ForAWorkflowTheLogicAppDoesntHave_Throws_RatherThanSayTheRunIsNoLongerKept()
    {
        // A wrong Logic App or workflow is a fault to see, not a run past its retention.
        _history.Answer(DefinitionAddress, HttpStatusCode.NotFound);

        var read = () => WorkflowRuns().RunAsync(Run, TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task BodiesAsync_ForAWorkflowTheLogicAppDoesntHave_Throws()
    {
        _history.Answer(DefinitionAddress, HttpStatusCode.NotFound);

        var fetch = () => WorkflowRuns().BodiesAsync(Run, "manual", TestContext.Current.CancellationToken);

        await fetch.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task RunAsync_WhenTheWorkflowComesWithoutItsDefinition_ShowsTheStepsWithoutSecureMarks()
    {
        _history
            .Answer(DefinitionAddress, new JsonObject { ["name"] = Workflow, ["properties"] = new JsonObject() })
            .Run("Succeeded", "2026-09-26T10:00:00Z", "2026-09-26T10:00:01Z", Trigger("manual", "Succeeded", 0, 0.05))
            .Answer(ActionsAddress, Actions());

        var run = await WorkflowRuns().RunAsync(Run, TestContext.Current.CancellationToken);

        run!.Steps.Should().ContainSingle().Which.SecureInputs.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WithoutAnEstate_SaysTheConsoleIsntConnected()
    {
        var read = () => WorkflowRuns(resourceGroupId: "").RunAsync(Run, TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Estate:ResourceGroupId*");
    }

    [Fact]
    public async Task FailedRunsAsync_ForTheEstate_ListsEachLogicAppsWorkflowsFailedRunsOfTheWindow()
    {
        const string Answer = "logic-answer-def456";
        _history
            .Answer(SitesAddress, Page(Site(LogicApp, "functionapp,workflowapp"), Site("func-other", "functionapp,linux"), Site(Answer, "functionapp,workflowapp")))
            .Answer(WorkflowsAddress(LogicApp), new JsonArray(new JsonObject { ["name"] = Workflow }))
            .Answer(WorkflowsAddress(Answer), Page(new JsonObject { ["name"] = "carrier-answer" }))
            .Answer(FailedRunsAddress(LogicApp, Workflow, Started), Page(RunOf("run-1", 5), RunOf("run-2", 9)))
            .Answer(FailedRunsAddress(Answer, "carrier-answer", Started), Page())
            .Answer(RunActionsAddress(LogicApp, Workflow, "run-1"), Actions(
                Action("Track_the_booking", "Succeeded", 0, 1, tracked: ("transport-order-intake", "corr-1")),
                Action("Submit_to_the_TMS", "Failed", 1, 3, error: ("ServiceUnavailable", "The TMS answered 503.")),
                Action("Scope", "Failed", 0, 4, error: ("ActionFailed", "An action failed."))))
            .Answer(RunActionsAddress(LogicApp, Workflow, "run-2"), Actions(
                Action("Submit_to_the_TMS", "TimedOut", 1, 3)));

        var failed = await WorkflowRuns().FailedRunsAsync(Started, TestContext.Current.CancellationToken);

        failed.Should().BeEquivalentTo(new[]
        {
            new RecordedFailedRun(new WorkflowRun(LogicApp, Workflow, "run-1"), Started.AddSeconds(5), "transport-order-intake", "corr-1",
                "Submit_to_the_TMS", new StepError("ServiceUnavailable", "The TMS answered 503.")),
            new RecordedFailedRun(new WorkflowRun(LogicApp, Workflow, "run-2"), Started.AddSeconds(9), null, null, "Submit_to_the_TMS", null),
        });
    }

    [Fact]
    public async Task FailedRunsAsync_ForARunWithNoFailedStep_StillListsIt()
    {
        _history
            .Answer(SitesAddress, Page(Site(LogicApp, "functionapp,workflowapp")))
            .Answer(WorkflowsAddress(LogicApp), Page(new JsonObject { ["name"] = Workflow }))
            .Answer(FailedRunsAddress(LogicApp, Workflow, Started), Page(RunOf("run-1", 5)))
            .Answer(RunActionsAddress(LogicApp, Workflow, "run-1"), Actions());

        var failed = await WorkflowRuns().FailedRunsAsync(Started, TestContext.Current.CancellationToken);

        failed.Should().ContainSingle().Which.Should().Be(new RecordedFailedRun(new WorkflowRun(LogicApp, Workflow, "run-1"), Started.AddSeconds(5), null, null, null, null));
    }

    [Fact]
    public async Task FailedRunsAsync_ForALongList_FollowsItsNextLink()
    {
        var next = $"{WorkflowsAddress(LogicApp)}&$skiptoken=2";
        _history
            .Answer(SitesAddress, Page(Site(LogicApp, "functionapp,workflowapp")))
            .Answer(WorkflowsAddress(LogicApp), Page(new JsonObject { ["name"] = Workflow }, nextLink: next))
            .Answer(next, Page(new JsonObject { ["name"] = "second-flow" }))
            .Answer(FailedRunsAddress(LogicApp, Workflow, Started), Page())
            .Answer(FailedRunsAddress(LogicApp, "second-flow", Started), Page(RunOf("run-9", 1)))
            .Answer(RunActionsAddress(LogicApp, "second-flow", "run-9"), Actions());

        var failed = await WorkflowRuns().FailedRunsAsync(Started, TestContext.Current.CancellationToken);

        failed.Should().ContainSingle().Which.Run!.Workflow.Should().Be("second-flow");
    }

    [Fact]
    public async Task FailedRunsAsync_Always_ReadsAsTheConsoleAndOnlyReads()
    {
        _history.Answer(SitesAddress, Page());

        await WorkflowRuns().FailedRunsAsync(Started, TestContext.Current.CancellationToken);

        _history.Requests.Should().OnlyContain(request => request.Method == HttpMethod.Get && request.Headers.Authorization!.Parameter == "console-token");
    }

    [Fact]
    public async Task FailedRunsAsync_WithoutAnEstate_SaysTheConsoleIsNotConnected()
    {
        var act = () => WorkflowRuns(resourceGroupId: "").FailedRunsAsync(Started, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*isn't connected to an estate*");
    }

    private static string SitesAddress => $"https://management.azure.com{ResourceGroupId}/providers/Microsoft.Web/sites?api-version=2023-12-01";

    private static string WorkflowsAddress(string app) =>
        $"https://management.azure.com{ResourceGroupId}/providers/Microsoft.Web/sites/{app}/hostruntime/runtime/webhooks/workflow/api/management/workflows?api-version=2018-11-01";

    private static string FailedRunsAddress(string app, string workflow, DateTimeOffset since) =>
        $"https://management.azure.com{ResourceGroupId}/providers/Microsoft.Web/sites/{app}/hostruntime/runtime/webhooks/workflow/api/management/workflows/{workflow}/runs?api-version=2018-11-01&$filter=" +
        Uri.EscapeDataString($"status eq 'Failed' and startTime ge {since:yyyy-MM-ddTHH:mm:ssZ}");

    private static string RunActionsAddress(string app, string workflow, string run) =>
        $"https://management.azure.com{ResourceGroupId}/providers/Microsoft.Web/sites/{app}/hostruntime/runtime/webhooks/workflow/api/management/workflows/{workflow}/runs/{run}/actions?api-version=2018-11-01";

    private static JsonObject Site(string name, string kind) => new() { ["name"] = name, ["kind"] = kind };

    private static JsonObject RunOf(string name, double start) =>
        new() { ["name"] = name, ["properties"] = new JsonObject { ["status"] = "Failed", ["startTime"] = Started.AddSeconds(start).ToString("O") } };

    private static JsonObject Page(params JsonObject[] items) => Page(items, null);

    private static JsonObject Page(JsonObject item, string nextLink) => Page([item], nextLink);

    private static JsonObject Page(JsonObject[] items, string? nextLink)
    {
        var page = new JsonObject { ["value"] = new JsonArray([.. items]) };
        if (nextLink is not null)
        {
            page["nextLink"] = nextLink;
        }

        return page;
    }

    private LogicAppsWorkflowRuns WorkflowRuns(string resourceGroupId = ResourceGroupId)
    {
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(Arg.Any<string>()).Returns(_http);
        return new LogicAppsWorkflowRuns(clients, _credential, Options.Create(new WorkflowRunsOptions { ResourceGroupId = resourceGroupId }));
    }

    private static JsonObject Step(params string[] secure)
    {
        var step = new JsonObject { ["type"] = "Http" };
        if (secure.Length > 0)
        {
            step["runtimeConfiguration"] = new JsonObject
            {
                ["secureData"] = new JsonObject { ["properties"] = new JsonArray([.. secure.Select(property => JsonValue.Create(property))]) },
            };
        }

        return step;
    }

    // A run carries its trigger as one object, its name beside its properties.
    private static JsonObject Trigger(string name, string status, double start, double end, string? inputsLink = null, string? outputsLink = null)
    {
        var trigger = (JsonObject)Properties(name, status, start, end, inputsLink, outputsLink, null)["properties"]!.DeepClone();
        trigger["name"] = name;
        return trigger;
    }

    private static JsonObject Action(
        string name,
        string status,
        double? start,
        double? end,
        string? inputsLink = null,
        string? outputsLink = null,
        (string Code, string Message)? error = null,
        (string Flow, string CorrelationId)? tracked = null)
    {
        var action = Properties(name, status, start, end, inputsLink, outputsLink, error);
        if (tracked is { } keys)
        {
            action["properties"]!["trackedProperties"] = new JsonObject { ["flow"] = keys.Flow, ["correlationId"] = keys.CorrelationId };
        }

        return action;
    }

    private static JsonObject Properties(
        string name,
        string status,
        double? start,
        double? end,
        string? inputsLink,
        string? outputsLink,
        (string Code, string Message)? error)
    {
        var properties = new JsonObject { ["status"] = status };
        if (start is { } from)
        {
            properties["startTime"] = Started.AddSeconds(from).ToString("O");
        }

        if (end is { } to)
        {
            properties["endTime"] = Started.AddSeconds(to).ToString("O");
        }

        if (inputsLink is not null)
        {
            properties["inputsLink"] = new JsonObject { ["uri"] = inputsLink, ["contentSize"] = 120 };
        }

        if (outputsLink is not null)
        {
            properties["outputsLink"] = new JsonObject { ["uri"] = outputsLink, ["contentSize"] = 120 };
        }

        if (error is { } failed)
        {
            properties["code"] = failed.Code;
            properties["error"] = new JsonObject { ["code"] = failed.Code, ["message"] = failed.Message };
        }

        return new JsonObject { ["name"] = name, ["properties"] = properties };
    }

    private static JsonObject Actions(params JsonObject[] actions) => new() { ["value"] = new JsonArray([.. actions]) };
}
