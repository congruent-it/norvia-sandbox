using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Agents.Runner;
using Norvia.Integration.BookingDraft.Api.Foundry;

namespace Norvia.Integration.BookingDraft.Api.UnitTests;

/// <summary>The Foundry client (ADR 0040) against a faked Responses endpoint: what it asks, what it makes of the answer, and that the Shipper's text is never logged.</summary>
public sealed class FoundryAgentClientTests : IDisposable
{
    private const string Text = "SECRET-CONSIGNEE-TEXT";
    private const string Draft = """{"booking": {}, "quotes": {}, "missing": []}""";

    private static readonly AgentQuestion Question = new($"<shipper_text>{Text}</shipper_text>", "corr-1", "run-42");

    private readonly FakeResponses _foundry = new();
    private readonly FakeLogCollector _logs = new();
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly BookingDraftActivities _activities;
    private readonly HttpClient _http;

    public FoundryAgentClientTests()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns("Norvia.Test.BookingDraft");
        _activities = new BookingDraftActivities(environment);
        _http = new HttpClient(_foundry);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Norvia.Test.BookingDraft",
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
            Microsoft.Extensions.Options.Options.Create(new BookingDraftHostOptions
            {
                TimeoutSeconds = timeoutSeconds,
                Foundry = new FoundryOptions { ProjectEndpoint = new Uri("https://ais-norvia.services.ai.azure.com/api/projects/norvia-intake") },
            }),
            credential,
            _activities,
            new FakeLogger<FoundryAgentClient>(_logs));
    }

    [Fact]
    public async Task AskAsync_AsksThePublishedAgentOfTheIntakeProject_StoresNothingAndMakesNoConversation()
    {
        _foundry.Answer(Message(Draft));

        var reply = await Client().AskAsync(Question, TestContext.Current.CancellationToken);

        reply.Should().Be(Draft);
        var create = _foundry.Requests.Single(request => request.Method == HttpMethod.Post);
        create.Uri.Should().Be("https://ais-norvia.services.ai.azure.com/api/projects/norvia-intake/openai/v1/responses");
        create.Authorization.Should().Be("Bearer foundry-token");
        var body = JsonNode.Parse(create.Body)!;
        body["agent_reference"]!["name"]!.GetValue<string>().Should().Be("booking-intake");
        body["agent_reference"]!["type"]!.GetValue<string>().Should().Be("agent_reference");
        body["store"]!.GetValue<bool>().Should().BeFalse();
        body["max_output_tokens"]!.GetValue<int>().Should().Be(1500, because: "the definition's cap");
        body["conversation"].Should().BeNull("no conversation is made (docs/operations/foundry.md, spike 3)");
        body["tools"].Should().BeNull("the Intake agent has no tool");
        body["input"]!.ToJsonString().Should().Contain(Text);
    }

    [Fact]
    public async Task AskAsync_JoinsTheTextOfEveryOutputPart()
    {
        _foundry.Answer(Message("""{"booking": {}, """), Message("""
            "quotes": {}, "missing": []}
            """));

        var reply = await Client().AskAsync(Question, TestContext.Current.CancellationToken);

        reply.Trim().Should().Be("""{"booking": {}, "quotes": {}, "missing": []}""");
    }

    [Fact]
    public async Task AskAsync_DeletesTheResponseBeforeItReturns()
    {
        _foundry.Answer(Message(Draft));

        await Client().AskAsync(Question, TestContext.Current.CancellationToken);

        _foundry.Requests.Select(request => request.Method).Should().Equal(HttpMethod.Post, HttpMethod.Delete);
        _foundry.Requests[1].Uri.Should().EndWith("/openai/v1/responses/resp-1");
    }

    [Fact]
    public async Task AskAsync_WhenTheResponseCannotBeDeleted_StillAnswers_AndCodesTheFailureOnTheSpan()
    {
        _foundry.Answer(Message(Draft));
        _foundry.DeleteStatus = HttpStatusCode.InternalServerError;

        var reply = await Client().AskAsync(Question, TestContext.Current.CancellationToken);

        reply.Should().Be(Draft);
        _logs.GetSnapshot().Should().Contain(log => log.Message.Contains(FoundryAgentClient.ConversationNotDeleted, StringComparison.Ordinal));
        _spans.Single().GetTagItem("failureCode.cleanup").Should().Be(FoundryAgentClient.ConversationNotDeleted);
    }

    [Fact]
    public async Task AskAsync_WhenTheResponseIsAlreadyGone_SaysNothingOfIt()
    {
        _foundry.Answer(Message(Draft));
        _foundry.DeleteStatus = HttpStatusCode.NotFound;

        await Client().AskAsync(Question, TestContext.Current.CancellationToken);

        _logs.GetSnapshot().Should().NotContain(log => log.Message.Contains(FoundryAgentClient.ConversationNotDeleted, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AskAsync_RecordsTheRunOnTheTrailWithTheAgentsIdentity_AndNeverTheText()
    {
        _foundry.Answer(Message(Draft));

        await Client().AskAsync(Question, TestContext.Current.CancellationToken);

        var log = _logs.GetSnapshot().Single(entry => entry.Message == "The Intake agent answered");
        var scope = log.Scopes.SelectMany(s => (IEnumerable<KeyValuePair<string, object?>>)s!).ToDictionary(pair => pair.Key, pair => pair.Value);
        scope["correlationId"].Should().Be("corr-1");
        scope["flow"].Should().Be("booking-draft");
        scope["agentId"].Should().Be("booking-intake");
        scope["runId"].Should().Be("run-42");
        scope["outcome"].Should().Be("Succeeded");
        _spans.Single().GetTagItem("agent.run.id").Should().Be("run-42");
        _spans.Single().GetTagItem("agent.name").Should().Be("booking-intake");
        EverythingRecorded().Should().NotContain(Text).And.NotContain("quotes");
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("incomplete")]
    [InlineData("cancelled")]
    public async Task AskAsync_WhenTheResponseDidNotComplete_Throws_AndNeverLogsTheText(string status)
    {
        _foundry.Answer(status, Message(Draft));

        var act = () => Client().AskAsync(Question, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AgentRunException>()).Which.FailureCode.Should().Be(AgentFailureCodes.AgentUnavailable);
        var failed = _logs.GetSnapshot().Single(entry => entry.Message.StartsWith("The Intake agent failed", StringComparison.Ordinal));
        failed.Scopes.SelectMany(s => (IEnumerable<KeyValuePair<string, object?>>)s!).Should().Contain(pair => pair.Key == "failureCode" && (string?)pair.Value == "AgentUnavailable");
        EverythingRecorded().Should().NotContain(Text);
        _foundry.Requests.Should().Contain(request => request.Method == HttpMethod.Delete, "a response made for a failed run is cleaned up too");
    }

    [Fact]
    public async Task AskAsync_WhenTheAgentCallsATool_Throws_AsRefused()
    {
        _foundry.Answer(new JsonObject { ["type"] = "mcp_call", ["name"] = "get_anything" }, Message(Draft));

        var act = () => Client().AskAsync(Question, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AgentRunException>()).Which.FailureCode.Should().Be(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task AskAsync_WhenFoundryAnswersAnError_Throws()
    {
        _foundry.Fail(HttpStatusCode.TooManyRequests);

        var act = () => Client().AskAsync(Question, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<AgentRunException>();
        _spans.Single().Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task AskAsync_WhenFoundryIsTooSlow_Throws_WithinTheDeadline()
    {
        _foundry.Hang = true;

        var act = () => Client(timeoutSeconds: 1).AskAsync(Question, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AskAsync_WhenTheCallerCancels_Throws_AndIsNotLoggedAsAFailure()
    {
        _foundry.Hang = true;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => Client().AskAsync(Question, cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _logs.GetSnapshot().Should().NotContain(entry => entry.Message.StartsWith("The Intake agent failed", StringComparison.Ordinal));
    }

    private string EverythingRecorded() =>
        string.Join('\n', _logs.GetSnapshot().Select(entry => entry.Message + string.Concat(entry.Scopes.SelectMany(s => (IEnumerable<KeyValuePair<string, object?>>)s!).Select(pair => $"{pair.Key}={pair.Value}"))))
        + string.Join('\n', _spans.SelectMany(span => span.TagObjects).Select(tag => $"{tag.Key}={tag.Value}"));

    private static JsonObject Message(string text) => new()
    {
        ["type"] = "message",
        ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }),
    };

    private sealed record Recorded(HttpMethod Method, string Uri, string? Authorization, string Body);

    private sealed class FakeResponses : HttpMessageHandler
    {
        private JsonObject[] _output = [];
        private string _status = "completed";
        private HttpStatusCode? _failure;

        public List<Recorded> Requests { get; } = [];

        public HttpStatusCode DeleteStatus { get; set; } = HttpStatusCode.OK;

        public bool Hang { get; set; }

        public void Answer(params JsonObject[] output) => _output = output;

        public void Answer(string status, params JsonObject[] output) => (_status, _output) = (status, output);

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
                ["output"] = new JsonArray(_output.Select(item => (JsonNode?)item.DeepClone()).ToArray()),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
