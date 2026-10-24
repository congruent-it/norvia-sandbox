using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Agents.Runner;
using Norvia.IntegrationConsole.Application.Agents;
using Norvia.IntegrationConsole.Infrastructure.Agents;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.Agents;

/// <summary>The Foundry client (ADR 0040) against a faked Responses endpoint: what it asks, what it makes of the answer, and that it never leaves a failure to the operator.</summary>
public sealed class FoundryAgentClientTests : IDisposable
{
    private const string CorrelationId = "c-1";

    private static readonly AgentRequest Request = new("integration-operations", "Explain Message trail c-1.", CorrelationId);

    private static readonly string Answer = """
        {"cause":"The Carrier sent DLV, which the map does not know.","evidence":[{"claim":"The tracking hop was dead-lettered as UnmappableTrackingMessage.","citation":"r1"}],"suggestedAction":"Ask the Carrier to send POD.","confidence":"high"}
        """;

    private readonly FakeResponses _foundry = new();
    private readonly FakeLogCollector _logs = new();
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly AgentActivities _activities = new(Substitute.For<IHostEnvironment>().WithName("Norvia.Test.Foundry"));
    private readonly HttpClient _http;

    public FoundryAgentClientTests()
    {
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
    public async Task AskAsync_AsksThePublishedAgent_WithTheCorrelationIdAsAStructuredInput_AndStoresNothing()
    {
        _foundry.Answer(Response(McpCall("get_message_trail"), Message(Answer)));

        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cause.Should().Contain("DLV");
        result.Value.Evidence.Should().ContainSingle().Which.ToolName.Should().Be("get_message_trail");
        var create = _foundry.Requests.Single(request => request.Method == HttpMethod.Post);
        create.Uri.Should().Be("https://ais-norvia.services.ai.azure.com/api/projects/norvia/openai/v1/responses");
        create.Authorization.Should().Be("Bearer foundry-token");
        var body = JsonNode.Parse(create.Body)!;
        body["agent_reference"]!["name"]!.GetValue<string>().Should().Be("integration-operations");
        body["agent_reference"]!["type"]!.GetValue<string>().Should().Be("agent_reference");
        body["structured_inputs"]!["correlationId"]!.GetValue<string>().Should().Be(CorrelationId);
        body["store"]!.GetValue<bool>().Should().BeFalse();
        body["max_tool_calls"]!.GetValue<int>().Should().Be(6, because: "the definition's cap");
        body["max_output_tokens"]!.GetValue<int>().Should().Be(1200);
        body["conversation"].Should().BeNull("no conversation is made (docs/operations/foundry.md, spike 3)");
        body["input"]!.ToJsonString().Should().Contain("Explain Message trail c-1.");
    }

    [Fact]
    public async Task AskAsync_DeletesTheResponseAfterwards_BestEffort()
    {
        _foundry.Answer(Response(McpCall("get_message_trail"), Message(Answer)));

        await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        _foundry.Requests.Should().Contain(request => request.Method == HttpMethod.Delete && request.Uri.EndsWith("/openai/v1/responses/resp-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AskAsync_WhenTheResponseCannotBeDeleted_StillAnswers_AndCodesTheFailure()
    {
        _foundry.Answer(Response(McpCall("get_message_trail"), Message(Answer)));
        _foundry.DeleteStatus = HttpStatusCode.InternalServerError;

        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _logs.GetSnapshot().Should().Contain(log => log.Message.Contains(FoundryAgentClient.ConversationNotDeleted, StringComparison.Ordinal));
        _spans.Single().GetTagItem("failureCode.cleanup").Should().Be(FoundryAgentClient.ConversationNotDeleted);
    }

    [Fact]
    public async Task AskAsync_WhenTheResponseIsAlreadyGone_SaysNothingOfIt()
    {
        _foundry.Answer(Response(McpCall("get_message_trail"), Message(Answer)));
        _foundry.DeleteStatus = HttpStatusCode.NotFound;

        await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        _logs.GetSnapshot().Should().NotContain(log => log.Message.Contains(FoundryAgentClient.ConversationNotDeleted, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AskAsync_NumbersTheResultsInTheOrderOfTheCalls()
    {
        const string answer = """
            {"cause":"c","evidence":[{"claim":"a","citation":"r1"},{"claim":"b","citation":"r2"}],"suggestedAction":"s","confidence":"medium"}
            """;
        _foundry.Answer(Response(McpCall("get_message_trail"), McpCall("get_run"), Message(answer)));

        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        result.Value.Evidence.Select(evidence => (evidence.ResultId, evidence.ToolName)).Should().Equal(("r1", "get_message_trail"), ("r2", "get_run"));
    }

    [Fact]
    public async Task AskAsync_RecordsTheRunAsASpanOfTheConsole_WithTheRunIdAndTheCorrelationId()
    {
        _foundry.Answer(Response(McpCall("get_message_trail"), Message(Answer)));

        await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        var span = _spans.Single();
        span.OperationName.Should().Be("Agent run");
        span.GetTagItem("agent.name").Should().Be("integration-operations");
        span.GetTagItem("agent.run.id").Should().Be("run-42");
        span.GetTagItem("correlationId").Should().Be(CorrelationId);
        span.GetTagItem("agent.response.id").Should().Be("resp-1");
        span.Status.Should().Be(ActivityStatusCode.Unset);
        var answered = _logs.GetSnapshot().Single(log => log.Message.Contains("answered", StringComparison.Ordinal));
        Scope(answered, "correlationId").Should().Be(CorrelationId);
        Scope(answered, "flow").Should().Be("integration-operations-agent");
        Scope(answered, "outcome").Should().Be("Succeeded");
    }

    [Fact]
    public async Task AskAsync_WhenFoundryAnswersAnError_IsUnavailable_AndSaysSoOnTheSpan()
    {
        _foundry.Fail(HttpStatusCode.ServiceUnavailable);

        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

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
    public async Task AskAsync_WhenTheResponseDidNotComplete_IsUnavailable(string status)
    {
        _foundry.Answer(Response(McpCall("get_message_trail"), Message(Answer)), status);

        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task AskAsync_WhenTheResponseCarriesAnError_IsUnavailable()
    {
        _foundry.Answer(Response(Message(Answer)), error: """{"code":"server_error","message":"x"}""");

        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task AskAsync_WhenAToolWasRefused_IsAgentToolRefused()
    {
        _foundry.Answer(Response(McpCall("get_message_trail", error: """{"code":403}"""), Message(Answer)));

        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task AskAsync_WhenAToolCallHasNoError_IsNotRefused_WhateverNullOrEmptyErrorFoundryWrites()
    {
        _foundry.Answer(Response(McpCall("get_message_trail", error: "null"), McpCall("get_message_trail", error: "\"\""), McpCall("get_message_trail", error: "{}"), Message(Answer)));

        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AskAsync_WhenTheAgentCalledAToolItsDefinitionDoesNotList_IsAgentToolRefused()
    {
        _foundry.Answer(Response(McpCall("resubmit_message"), Message(Answer)));

        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task AskAsync_WhenTheAgentMadeMoreCallsThanTheDefinitionAllows_IsAgentToolRefused()
    {
        var calls = Enumerable.Repeat(McpCall("get_message_trail"), 7).Append(Message(Answer)).ToArray();
        _foundry.Answer(Response(calls));

        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentToolRefused);
    }

    [Theory]
    [InlineData("this is not JSON")]
    [InlineData("""{"cause":"c","evidence":[{"claim":"a","citation":"r9"}],"suggestedAction":"s","confidence":"high"}""")]
    [InlineData("""{"cause":"c","evidence":[],"suggestedAction":"s","confidence":"high"}""")]
    public async Task AskAsync_WhenTheReplyIsNotASuggestionTheRunCanStandBehind_IsUnavailable(string reply)
    {
        _foundry.Answer(Response(McpCall("get_message_trail"), Message(reply)));

        var result = await Client().AskAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task AskAsync_WhenFoundryIsTooSlow_IsUnavailable_AfterTheTimeout()
    {
        _foundry.Hang = true;

        var result = await Client(timeoutSeconds: 1).AskAsync(Request, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task AskAsync_WhenTheOperatorCancels_Throws_AsNoFailureOfTheAgent()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _foundry.Hang = true;

        var ask = () => Client().AskAsync(Request, cancelled.Token);

        await ask.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AskAsync_ForAnAgentThatIsNotDefined_IsUnavailable()
    {
        var result = await Client().AskAsync(Request with { AgentName = "no-such-agent" }, TestContext.Current.CancellationToken);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
        _foundry.Requests.Should().BeEmpty();
    }

    private static object? Scope(Microsoft.Extensions.Logging.Testing.FakeLogRecord log, string key) =>
        log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).Where(pair => pair.Key == key).Select(pair => pair.Value).FirstOrDefault();

    private static JsonObject McpCall(string tool, string? error = null) => new()
    {
        ["type"] = "mcp_call",
        ["id"] = "mcp_1",
        ["server_label"] = "integration-operations",
        ["name"] = tool,
        ["arguments"] = "{}",
        ["output"] = "{}",
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
