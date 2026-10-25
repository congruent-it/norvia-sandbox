using System.Net;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.HealthCheck.Application.Narrate;
using Norvia.HealthCheck.Application.Run;
using static Norvia.HealthCheck.Agents.IntegrationTests.FakeAzure;
using static Norvia.HealthCheck.Agents.IntegrationTests.FakeFoundry;

namespace Norvia.HealthCheck.Agents.IntegrationTests;

/// <summary>
/// The Estate health check in Azure (#392) as the app builds it: the Azure adapter maps faked Resource Graph and Service Bus answers into the
/// snapshots' shape, so the checklist finds what it finds on a snapshot, and the Foundry client asks a faked project's Responses endpoint. The API
/// Management MCP server's policy and the drift test between the stand-in's tool list and the Bicep's are the architecture tests'
/// (<c>HealthCheckAgentAzureTests</c>, <c>bicep snapshot</c>, nothing deployed). Each test starts an app of its own.
/// </summary>
public sealed class AzureHealthCheckTests
{
    private const string Findings = """{"findings":[{"id":"F-DLQ-ALERT","checkId":"DLQ-ALERT"}]}""";

    private const string Answer = """
        {"summary":"One Finding.","findings":[{"id":"F-DLQ-ALERT","impact":"Nobody is told a tracking message was dead-lettered.","citation":"r1"}]}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunHealthCheck_WhenAnAlertIsMissing_ReportsTheFinding_NamingTheEntityWithoutOne()
    {
        await using var host = new AzureHost();
        host.Azure.Entities(["carrier-tracking", "carrier-proof-of-delivery"], ("shipment-events", "carrier-booking"))
            .Alerts(DeadLetterAlert("alert-dead-letter-carrier-proof-of-delivery", "carrier-proof-of-delivery"), DeadLetterAlert("alert-dead-letter-carrier-booking", "carrier-booking"));

        var report = await RunAsync(host);

        report.Estate.Should().Be("azure");
        var finding = report.Findings.Should().ContainSingle().Subject;
        finding.Id.Should().Be("F-DLQ-ALERT");
        finding.Evidence.Select(evidence => evidence.Subject).Should().Equal("carrier-tracking");
    }

    [Fact]
    public async Task RunHealthCheck_AfterTheAlertIsRestored_ReportsNoFinding()
    {
        await using var host = new AzureHost();
        host.Azure.Entities(["carrier-tracking"]).Alerts();
        (await RunAsync(host)).Findings.Should().ContainSingle();

        host.Azure.Alerts(DeadLetterAlert("alert-dead-letter-carrier-tracking", "carrier-tracking"));
        var fixedReport = await RunAsync(host);

        fixedReport.Findings.Should().BeEmpty();
        fixedReport.ChecksRun.Should().BeGreaterThan(0, "no Finding must mean every Check passed, not that none ran");
    }

    [Fact]
    public async Task RunHealthCheck_ReadsOnlyTheEstatesResourceGroupAndNamespace_AsTheAppsIdentity()
    {
        await using var host = new AzureHost();
        host.Azure.Entities(["carrier-tracking"]).Alerts(DeadLetterAlert("a"));

        await RunAsync(host);

        var requests = host.Azure.Requests;
        requests.Should().OnlyContain(request => request.Authorization == "Bearer app-token");
        requests.Should().OnlyContain(request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Get, "the adapter only reads");
        requests.Where(request => request.Method == HttpMethod.Post).Should().OnlyContain(
            request => request.Uri.Contains("ResourceGraph", StringComparison.Ordinal) && request.Body.Contains("00000000-0000-0000-0000-00000000000a", StringComparison.Ordinal) && request.Body.Contains("rg-norvia", StringComparison.Ordinal),
            "the only things posted are queries over the estate's resource group");
        requests.Where(request => request.Method == HttpMethod.Get).Should().OnlyContain(request => request.Uri.Contains(AzureHost.NamespaceId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunHealthCheck_WhenAzureRefuses_Fails_AndNoFindingIsInvented()
    {
        await using var host = new AzureHost();
        host.Azure.Fail(HttpStatusCode.Forbidden);

        var act = async () => await RunAsync(host);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Narrate_AsksThePublishedAgentOfTheProject_AsTheAppsIdentity_WithTheCorrelationIdAsAStructuredInput_AndStoresNothing()
    {
        await using var host = new AzureHost();
        host.Foundry.Answer(McpCall("run_checks", Findings), Message(Answer));
        var correlationId = Guid.NewGuid().ToString();

        var result = await NarrateAsync(host, correlationId);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        result.Value.Items.Select(item => (item.Id, item.ToolName)).Should().Equal([("F-DLQ-ALERT", "run_checks")]);
        var create = host.Foundry.Requests.Single(request => request.Method == HttpMethod.Post);
        create.Uri.Should().Be($"{AzureHost.Project}/openai/v1/responses");
        create.Authorization.Should().Be("Bearer app-token");
        var body = JsonNode.Parse(create.Body)!;
        body["agent_reference"]!["name"]!.GetValue<string>().Should().Be("estate-health-check");
        body["structured_inputs"]!["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        body["store"]!.GetValue<bool>().Should().BeFalse();
        host.Foundry.Requests.Should().Contain(request => request.Method == HttpMethod.Delete && request.Uri.EndsWith("/openai/v1/responses/resp-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Narrate_WhenTheNarrationExplainsAFindingTheToolDidNotReturn_IsRefused()
    {
        await using var host = new AzureHost();
        host.Foundry.Answer(McpCall("run_checks", """{"findings":[]}"""), Message(Answer));

        var result = await NarrateAsync(host, Guid.NewGuid().ToString());

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Unavailable);
    }

    [Fact]
    public async Task Narrate_WhenTheAgentCalledAToolItsDefinitionDoesNotList_IsAgentToolRefused()
    {
        await using var host = new AzureHost();
        host.Foundry.Answer(McpCall("fix_estate", Findings), Message(Answer));

        var result = await NarrateAsync(host, Guid.NewGuid().ToString());

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain("AgentToolRefused");
    }

    [Fact]
    public async Task Narrate_WhenTheProjectAnswersAnError_IsUnavailable_WithoutARetry()
    {
        await using var host = new AzureHost();
        host.Foundry.Fail(HttpStatusCode.ServiceUnavailable);

        var result = await NarrateAsync(host, Guid.NewGuid().ToString());

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain("AgentUnavailable");
        host.Foundry.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Post);
    }

    private static async Task<HealthCheckReport> RunAsync(AzureHost host)
    {
        using var scope = host.AppServices.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RunHealthCheckQuery(), Ct);
        return result.Value;
    }

    private static async Task<Result<Norvia.Agents.Narration>> NarrateAsync(AzureHost host, string correlationId)
    {
        using var scope = host.AppServices.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new NarrateHealthCheckQuery(correlationId), Ct);
    }
}
