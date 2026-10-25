using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Agents.Runner;
using Norvia.HealthCheck.Application.Agents;
using Norvia.HealthCheck.Infrastructure.Agents;

namespace Norvia.HealthCheck.Infrastructure.UnitTests.Agents;

/// <summary>The Foundry client (ADR 0040) against a faked Responses endpoint: what it asks, what it makes of the answer, and that it never leaves a failure to the operator.</summary>
public sealed class FoundryAgentClientTests : IDisposable
{
    private const string CorrelationId = "run-1";

    private static readonly AgentRequest Request = new("estate-health-check", HealthCheckQuestions.Narrate, CorrelationId);

    private const string Findings = """{"findings":[{"id":"F-DLQ-ALERT","checkId":"DLQ-ALERT"}]}""";

    private static readonly string Answer = """
        {"summary":"One Finding.","findings":[{"id":"F-DLQ-ALERT","impact":"Nobody is told a tracking message was dead-lettered.","citation":"r1"}]}
        """;

    private readonly FakeResponses _foundry = new();
    private readonly FakeLogCollector _logs = new();
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly AgentActivities _activities;
    private readonly HttpClient _http;

    public FoundryAgentClientTests()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns("Norvia.Test.Foundry");
        _activities = new AgentActivities(environment);
        _http = new HttpClient(_foundry);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Norvia.Test.Foundry",
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
        _foundry.Dispose();
    }

    private FoundryAgentClient Client(int timeoutSeconds = 5)
    {
        var credential = Substitute.For<TokenCredential>();
        credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>()).Returns(new AccessToken("foundry-token", DateTimeOffset.MaxValue));
        return new FoundryAgentClient(
            _http,
            Microsoft.Extensions.Options.Options.Create(new AgentClientOptions
            {
                TimeoutSeconds = timeoutSeconds,
                Foundry = new FoundryOptions { ProjectEndpoint = new Uri("https://ais-norvia.services.ai.azure.com/api/projects/norvia") },
            }),
            credential,
            new FixedRunIds("run-42"),
            _activities,
            new FakeLogger<FoundryAgentClient>(_logs));
    }

    [Fact]
    public async Task NarrateAsync_AsksThePublishedAgent_WithTheCorrelationIdAsAStructuredInput_AndStoresNothing()
    {
        _foundry.Answer(Response(McpCall("run_checks"), Message(Answer)));

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle().Which.Should().Match<Norvia.Agents.NarratedItem>(item => item.Id == "F-DLQ-ALERT" && item.ToolName == "run_checks" && item.ResultId == "r1");
        result.Value.Run.Should().Be(new Norvia.Agents.AgentRun("estate-health-check", "run-42"));
        var create = _foundry.Requests.Single(request => request.Method == HttpMethod.Post);
        create.Uri.Should().Be("https://ais-norvia.services.ai.azure.com/api/projects/norvia/openai/v1/responses");
        create.Authorization.Should().Be("Bearer foundry-token");
        var body = JsonNode.Parse(create.Body)!;
        body["agent_reference"]!["name"]!.GetValue<string>().Should().Be("estate-health-check");
        body["agent_reference"]!["type"]!.GetValue<string>().Should().Be("agent_reference");
        body["structured_inputs"]!["correlationId"]!.GetValue<string>().Should().Be(CorrelationId);
        body["store"]!.GetValue<bool>().Should().BeFalse();
        body["max_tool_calls"]!.GetValue<int>().Should().Be(4, because: "the definition's cap");
        body["max_output_tokens"]!.GetValue<int>().Should().Be(1500);
        body["conversation"].Should().BeNull("no conversation is made (docs/operations/foundry.md, spike 3)");
    }

    [Fact]
    public async Task NarrateAsync_DeletesTheResponseAfterwards_BestEffort()
    {
        _foundry.Answer(Response(McpCall("run_checks"), Message(Answer)));

        await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        _foundry.Requests.Should().Contain(request => request.Method == HttpMethod.Delete && request.Uri.EndsWith("/openai/v1/responses/resp-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NarrateAsync_WhenTheResponseCannotBeDeleted_StillAnswers_AndCodesTheFailure()
    {
        _foundry.Answer(Response(McpCall("run_checks"), Message(Answer)));
        _foundry.DeleteStatus = HttpStatusCode.InternalServerError;

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _logs.GetSnapshot().Should().Contain(log => log.Message.Contains(FoundryAgentClient.ConversationNotDeleted, StringComparison.Ordinal));
        _spans.Single().GetTagItem("failureCode.cleanup").Should().Be(FoundryAgentClient.ConversationNotDeleted);
    }

    [Fact]
    public async Task NarrateAsync_WhenTheResponseIsAlreadyGone_SaysNothingOfIt()
    {
        _foundry.Answer(Response(McpCall("run_checks"), Message(Answer)));
        _foundry.DeleteStatus = HttpStatusCode.NotFound;

        await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        _logs.GetSnapshot().Should().NotContain(log => log.Message.Contains(FoundryAgentClient.ConversationNotDeleted, StringComparison.Ordinal));
    }

    [Fact]
    public async Task NarrateAsync_RecordsTheRunAsASpanOfTheApp_WithTheRunIdAndTheCorrelationId()
    {
        _foundry.Answer(Response(McpCall("run_checks"), Message(Answer)));

        await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        var span = _spans.Single();
        span.OperationName.Should().Be("Agent run");
        span.GetTagItem("agent.name").Should().Be("estate-health-check");
        span.GetTagItem("agent.run.id").Should().Be("run-42");
        span.GetTagItem("correlationId").Should().Be(CorrelationId);
        span.GetTagItem("agent.response.id").Should().Be("resp-1");
        span.Status.Should().Be(ActivityStatusCode.Unset);
        var answered = _logs.GetSnapshot().Single(log => log.Message.Contains("answered", StringComparison.Ordinal));
        Scope(answered, "correlationId").Should().Be(CorrelationId);
        Scope(answered, "flow").Should().Be("estate-health-check-agent");
        Scope(answered, "outcome").Should().Be("Succeeded");
    }

    [Fact]
    public async Task NarrateAsync_WhenFoundryAnswersAnError_IsUnavailable_AndSaysSoOnTheSpan()
    {
        _foundry.Fail(HttpStatusCode.ServiceUnavailable);

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.Status.Should().Be(Ardalis.Result.ResultStatus.Unavailable);
        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
        _spans.Single().Status.Should().Be(ActivityStatusCode.Error);
        var failed = _logs.GetSnapshot().Single(log => log.Message.Contains("failed", StringComparison.Ordinal));
        Scope(failed, "failureCode").Should().Be(AgentFailureCodes.AgentUnavailable);
        Scope(failed, "outcome").Should().Be("Failed");
        _foundry.Requests.Should().ContainSingle("there is no retry");
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("incomplete")]
    [InlineData("cancelled")]
    public async Task NarrateAsync_WhenTheResponseDidNotComplete_IsUnavailable(string status)
    {
        _foundry.Answer(Response(McpCall("run_checks"), Message(Answer)), status);

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task NarrateAsync_WhenTheResponseCarriesAnError_IsUnavailable()
    {
        _foundry.Answer(Response(Message(Answer)), error: """{"code":"server_error","message":"x"}""");

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task NarrateAsync_WhenAToolWasRefused_IsAgentToolRefused()
    {
        _foundry.Answer(Response(McpCall("run_checks", error: """{"code":403}"""), Message(Answer)));

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task NarrateAsync_WhenAToolCallHasNoError_IsNotRefused_WhateverNullOrEmptyErrorFoundryWrites()
    {
        _foundry.Answer(Response(McpCall("run_checks", error: "null"), McpCall("run_checks", error: "\"\""), Message(Answer)));

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task NarrateAsync_WhenTheAgentCalledAToolItsDefinitionDoesNotList_IsAgentToolRefused()
    {
        _foundry.Answer(Response(McpCall("fix_estate"), Message(Answer)));

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task NarrateAsync_WhenTheAgentMadeMoreCallsThanTheDefinitionAllows_IsAgentToolRefused()
    {
        _foundry.Answer(Response(McpCall("run_checks"), McpCall("run_checks"), McpCall("run_checks"), McpCall("run_checks"), McpCall("run_checks"), Message(Answer)));

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentToolRefused);
    }

    [Theory]
    [InlineData("this is not JSON")]
    [InlineData("""{"summary":"s","findings":[{"id":"F-DLQ-ALERT","impact":"i","citation":"r9"}]}""")]
    [InlineData("""{"summary":"s","findings":[{"id":"F-INVENTED","impact":"i","citation":"r1"}]}""")]
    public async Task NarrateAsync_WhenTheReplyExplainsAFindingTheRunNeverHad_IsUnavailable(string reply)
    {
        _foundry.Answer(Response(McpCall("run_checks"), Message(reply)));

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task NarrateAsync_WhenFoundryHandedOverNoToolAnswer_RefusesAnyFinding_BecauseNothingBacksIt()
    {
        _foundry.Answer(Response(McpCall("run_checks", output: null), Message(Answer)));

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task NarrateAsync_WhenEveryCheckPassed_NarratesNoFinding()
    {
        _foundry.Answer(Response(McpCall("run_checks", output: """{"findings":[]}"""), Message("""{"summary":"All the checks passed.","findings":[]}""")));

        var result = await Client().NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.Value.Items.Should().BeEmpty();
        result.Value.Summary.Should().Be("All the checks passed.");
    }

    [Fact]
    public async Task NarrateAsync_WhenFoundryIsTooSlow_IsUnavailable_AfterTheTimeout()
    {
        _foundry.Hang = true;

        var result = await Client(timeoutSeconds: 1).NarrateAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task NarrateAsync_WhenTheOperatorCancels_Throws_AsNoFailureOfTheAgent()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _foundry.Hang = true;

        var narrate = () => Client().NarrateAsync(Request, cancelled.Token);

        await narrate.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NarrateAsync_ForAnAgentThatIsNotDefined_IsUnavailable()
    {
        var result = await Client().NarrateAsync(Request with { AgentName = "no-such-agent" }, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
        _foundry.Requests.Should().BeEmpty();
    }

    private static object? Scope(Microsoft.Extensions.Logging.Testing.FakeLogRecord log, string key) =>
        log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).Where(pair => pair.Key == key).Select(pair => pair.Value).FirstOrDefault();

    private static JsonObject McpCall(string tool, string? error = null, string? output = Findings) => new()
    {
        ["type"] = "mcp_call",
        ["id"] = "mcp_1",
        ["server_label"] = "health_check",
        ["name"] = tool,
        ["arguments"] = "{}",
        ["output"] = output,
        ["error"] = error is null ? null : JsonNode.Parse(error),
    };

    private static JsonObject Message(string text) => new()
    {
        ["type"] = "message",
        ["role"] = "assistant",
        ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }),
    };

    private static JsonObject[] Response(params JsonObject[] output) => output;

    private sealed class FixedRunIds(string id) : IRunIds
    {
        public string Next() => id;
    }

    private sealed record Recorded(HttpMethod Method, string Uri, string? Authorization, string Body);

    /// <summary>The project's Responses endpoint: the first POST is answered from the test, a DELETE as the test says.</summary>
    private sealed class FakeResponses : HttpMessageHandler
    {
        private JsonObject[] _output = [];
        private string _status = "completed";
        private string? _error;
        private HttpStatusCode? _failure;

        public List<Recorded> Requests { get; } = [];

        public HttpStatusCode DeleteStatus { get; set; } = HttpStatusCode.OK;

        public bool Hang { get; set; }

        public void Answer(JsonObject[] output, string status = "completed", string? error = null)
        {
            (_output, _status, _error) = (output, status, error);
        }

        public void Fail(HttpStatusCode status) => _failure = status;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Recorded(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body));
            if (request.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(DeleteStatus);
            }

            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (_failure is { } failure)
            {
                return new HttpResponseMessage(failure);
            }

            var response = new JsonObject
            {
                ["id"] = "resp-1",
                ["object"] = "response",
                ["status"] = _status,
                ["error"] = _error is null ? null : JsonNode.Parse(_error),
                ["output"] = new JsonArray(_output.Select(item => (JsonNode?)item.DeepClone()).ToArray()),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
