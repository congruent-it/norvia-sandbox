using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Erp.Application.Agents;
using Norvia.Erp.Infrastructure.Agents;

namespace Norvia.Erp.Infrastructure.UnitTests.Agents;

public sealed class LocalAgentClientTests : IDisposable
{
    private const string CorrelationId = "c-1";

    private static readonly AgentRequest Request = new("finance-matching", FinanceQuestions.ExplainInvoiceLine("CAR-0001", "VDT-1", "TRX-1"), CorrelationId);

    private readonly FakeMcpServer _mcp = new();
    private readonly FakeLogCollector _logs = new();
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly AgentActivities _activities = new(Substitute.For<IHostEnvironment>().WithName("Norvia.Test.Erp"));
    private readonly HttpClient _http;

    public LocalAgentClientTests()
    {
        _http = new HttpClient(_mcp);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Norvia.Test.Erp",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = _spans.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _activities.Dispose();
        _http.Dispose();
    }

    private LocalAgentClient Client(AgentClientOptions? options = null) =>
        new(
            _http,
            Microsoft.Extensions.Options.Options.Create(options ?? new AgentClientOptions { McpAddress = new Uri("http://mcp.test/mcp/finance-matching"), TimeoutSeconds = 5 }),
            new FixedRunIds("run-42"),
            _activities,
            new FakeLogger<LocalAgentClient>(_logs));

    private Dictionary<string, object?> ScopeOf(string outcome) =>
        _logs.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary())
            .Last(keys => keys.TryGetValue("outcome", out var value) && Equals(value, outcome));

    [Fact]
    public async Task AskAsync_WithTheScriptedModel_ExplainsThePriceMismatch()
    {
        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cause.Should().Contain("€80.00").And.Contain("€70.00");
        result.Value.Evidence.Should().OnlyContain(evidence => evidence.ToolName == "get_invoice_line");
        result.Value.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
        result.Value.Run.Should().Be(new AgentRun("finance-matching", "run-42"), because: "the audit names the run if the operator acts on the Suggestion");
        _mcp.CorrelationIds.Should().NotBeEmpty().And.OnlyContain(id => id == CorrelationId, because: "the run passes its correlation ID down to every tool call");
    }

    [Fact]
    public async Task AskAsync_CallsTheMcpServerWithTheAgentsOwnToken()
    {
        var withToken = new HttpClient(new BearerTokenHandler(Microsoft.Extensions.Options.Options.Create(SignedOptions()), TimeProvider.System) { InnerHandler = _mcp });
        var client = new LocalAgentClient(
            withToken, Microsoft.Extensions.Options.Options.Create(SignedOptions()), new FixedRunIds("run-42"), _activities, new FakeLogger<LocalAgentClient>(_logs));

        await client.AskAsync(Request, TestContext.Current.CancellationToken);

        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(_mcp.Authorizations.First()["Bearer ".Length..]);
        token.GetClaim("azp").Value.Should().Be("norvia-local-agent-finance-matching");
        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal("CarrierInvoices.FinanceAgent.Read");
        token.Audiences.Should().Equal("api://norvia-finance-matching-agent");
        token.Issuer.Should().Be("https://norvia.local/tokens");
    }

    [Fact]
    public async Task AskAsync_IsASpanOfTheErpUi_WithTheRunId_AndTheTrailsCorrelationId()
    {
        await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        var span = _spans.Should().ContainSingle().Subject;
        span.OperationName.Should().Be("Agent run");
        span.GetTagItem("agent.run.id").Should().Be("run-42");
        span.GetTagItem("agent.name").Should().Be("finance-matching");
        span.GetTagItem("correlationId").Should().Be(CorrelationId);
        span.Status.Should().NotBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task AskAsync_IsATraceOnTheTrail_WithTheFlowAndTheOutcome()
    {
        await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        var keys = ScopeOf("Succeeded");
        keys["correlationId"].Should().Be(CorrelationId);
        keys["flow"].Should().Be("finance-matching-agent");
    }

    [Fact]
    public async Task AskAsync_TheMcpServerRefusesTheTool_IsAgentToolRefused_AndTheSpanFailed()
    {
        _mcp.ToolsRefused = true;

        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        result.Status.Should().Be(Ardalis.Result.ResultStatus.Unavailable);
        result.Errors.Should().Equal(AgentFailureCodes.AgentToolRefused);
        _spans.Single().Status.Should().Be(ActivityStatusCode.Error);
        var keys = ScopeOf("Failed");
        keys["failureCode"].Should().Be(AgentFailureCodes.AgentToolRefused);
        keys["correlationId"].Should().Be(CorrelationId);
    }

    [Fact]
    public async Task AskAsync_TheMcpServerIsDown_IsAgentUnavailable()
    {
        _mcp.Unreachable = new HttpRequestException("connection refused");

        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
        ScopeOf("Failed")["failureCode"].Should().Be(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task AskAsync_WithoutAnMcpAddress_IsAgentUnavailable()
    {
        var result = await Client(new AgentClientOptions()).AskAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task AskAsync_ForAnAgentWithNoDefinition_IsAgentUnavailable()
    {
        var result = await Client().AskAsync(Request with { AgentName = "no-such-agent" }, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    private static AgentClientOptions SignedOptions() => new()
    {
        McpAddress = new Uri("http://mcp.test/mcp/finance-matching"),
        TimeoutSeconds = 5,
        Tokens = new AgentTokenOptions
        {
            Issuer = "https://norvia.local/tokens",
            Audience = "api://norvia-finance-matching-agent",
            ClientId = "norvia-local-agent-finance-matching",
            Role = "CarrierInvoices.FinanceAgent.Read",
            SigningKey = Convert.ToBase64String(new byte[64].Select((_, index) => (byte)(index + 1)).ToArray()),
        },
    };

    private sealed class FixedRunIds(string id) : IRunIds
    {
        public string Next() => id;
    }
}

internal static class EnvironmentExtensions
{
    public static IHostEnvironment WithName(this IHostEnvironment environment, string name)
    {
        environment.ApplicationName.Returns(name);
        return environment;
    }
}
