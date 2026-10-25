using System.Diagnostics;
using Azure.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Agents.Runner;
using Norvia.HealthCheck.Application.Agents;
using Norvia.HealthCheck.Infrastructure.Agents;

namespace Norvia.HealthCheck.Infrastructure.UnitTests.Agents;

public sealed class LocalAgentClientTests : IDisposable
{
    private const string CorrelationId = "run-1";

    private static readonly AgentRequest Request = new("estate-health-check", HealthCheckQuestions.Narrate, CorrelationId);

    private readonly FakeMcpServer _mcp = new();
    private readonly FakeLogCollector _logs = new();
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly AgentActivities _activities;
    private readonly HttpClient _http;

    public LocalAgentClientTests()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns("Norvia.Test.HealthCheck");
        _activities = new AgentActivities(environment);
        _http = new HttpClient(_mcp);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Norvia.Test.HealthCheck",
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

    private LocalAgentClient Client(AgentClientOptions? options = null, HttpClient? http = null) =>
        new(
            http ?? _http,
            Microsoft.Extensions.Options.Options.Create(options ?? new AgentClientOptions { McpAddress = new Uri("http://mcp.test/mcp/health-check"), TimeoutSeconds = 5 }),
            Substitute.For<TokenCredential>(),
            new FixedRunIds("run-42"),
            _activities,
            new FakeLogger<LocalAgentClient>(_logs));

    private Dictionary<string, object?> ScopeOf(string outcome) =>
        _logs.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary())
            .Last(keys => keys.TryGetValue("outcome", out var value) && Equals(value, outcome));

    [Fact]
    public async Task NarrateAsync_WithTheScriptedModel_NarratesTheFindingTheToolReturned()
    {
        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle().Which.Should().Match<Norvia.Agents.NarratedItem>(item => item.Id == "F-DLQ-ALERT" && item.ToolName == "run_checks" && item.ResultId == "r1");
        result.Value.Run.Should().Be(new Norvia.Agents.AgentRun("estate-health-check", "run-42"));
        _mcp.CorrelationIds.Should().NotBeEmpty().And.OnlyContain(id => id == CorrelationId, because: "the run passes its correlation ID down to every tool call");
    }

    [Fact]
    public async Task NarrateAsync_WhenTheToolReturnsNoFinding_SaysSo()
    {
        _mcp.Answer = FakeMcpServer.NoFinding;

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.Summary.Should().Contain("found nothing");
    }

    [Fact]
    public async Task NarrateAsync_CallsTheMcpServerWithTheAgentsOwnToken_HoldingOnlyTheCallRole()
    {
        var withToken = new HttpClient(new BearerTokenHandler(Microsoft.Extensions.Options.Options.Create(SignedOptions()), TimeProvider.System) { InnerHandler = _mcp });

        await Client(SignedOptions(), withToken).NarrateAsync(Request, TestContext.Current.CancellationToken);

        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(_mcp.Authorizations.First()["Bearer ".Length..]);
        token.GetClaim("azp").Value.Should().Be("norvia-local-agent-estate-health-check");
        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal("HealthCheck.Agent.Read");
        token.Audiences.Should().Equal("api://norvia-health-check-agent");
    }

    [Fact]
    public async Task NarrateAsync_IsASpanOfTheApp_WithTheRunId_AndTheRunsCorrelationId()
    {
        await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        var span = _spans.Should().ContainSingle().Subject;
        span.OperationName.Should().Be("Agent run");
        span.GetTagItem("agent.run.id").Should().Be("run-42");
        span.GetTagItem("agent.name").Should().Be("estate-health-check");
        span.GetTagItem("correlationId").Should().Be(CorrelationId);
        span.Status.Should().NotBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task NarrateAsync_IsATraceOnTheTrail_WithTheFlowAndTheOutcome()
    {
        await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        var keys = ScopeOf("Succeeded");
        keys["correlationId"].Should().Be(CorrelationId);
        keys["flow"].Should().Be("estate-health-check-agent");
    }

    [Fact]
    public async Task NarrateAsync_TheMcpServerRefusesTheTool_IsAgentToolRefused_AndTheSpanFailed()
    {
        _mcp.ToolsRefused = true;

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.Status.Should().Be(Ardalis.Result.ResultStatus.Unavailable);
        result.Errors.Should().Equal(AgentFailureCodes.AgentToolRefused);
        _spans.Single().Status.Should().Be(ActivityStatusCode.Error);
        ScopeOf("Failed")["failureCode"].Should().Be(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task NarrateAsync_TheMcpServerIsDown_IsAgentUnavailable()
    {
        _mcp.Unreachable = new HttpRequestException("connection refused");

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
        ScopeOf("Failed")["failureCode"].Should().Be(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task NarrateAsync_WithoutAnMcpAddress_IsAgentUnavailable()
    {
        var result = await Client(new AgentClientOptions()).NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task NarrateAsync_ForAnAgentWithNoDefinition_IsAgentUnavailable()
    {
        var result = await Client().NarrateAsync(Request with { AgentName = "no-such-agent" }, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    private static AgentClientOptions SignedOptions() => new()
    {
        McpAddress = new Uri("http://mcp.test/mcp/health-check"),
        TimeoutSeconds = 5,
        Tokens = new AgentTokenOptions
        {
            Issuer = "https://norvia.local/tokens",
            Audience = "api://norvia-health-check-agent",
            ClientId = "norvia-local-agent-estate-health-check",
            Role = "HealthCheck.Agent.Read",
            SigningKey = Convert.ToBase64String(new byte[64].Select((_, index) => (byte)(index + 1)).ToArray()),
        },
    };

    private sealed class FixedRunIds(string id) : IRunIds
    {
        public string Next() => id;
    }
}
