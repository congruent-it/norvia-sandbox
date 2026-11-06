using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Azure.Core;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Agents.Runner;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Personas;
using Norvia.Simulator.Infrastructure.PartnerApi;
using Norvia.Simulator.Infrastructure.Personas;

namespace Norvia.Simulator.Infrastructure.UnitTests.Personas;

/// <summary>The Foundry client (ADR 0040) against a faked Responses endpoint: what it asks, what it makes of the answer, and that a failure is never thrown.</summary>
public sealed class FoundryAgentClientTests : IDisposable
{
    private static readonly PoolRequest Request = new(new PersonaProfile("terse-dutch", "nl", "a few blunt words"), TextCategory.DeclineReason, 3);

    private readonly FakeResponses _foundry = new();
    private readonly FakeLogCollector _logs = new();
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly SimulatorActivities _activities = new("Norvia.Test.SimulatorFoundry");
    private readonly HttpClient _http;

    public FoundryAgentClientTests()
    {
        _http = new HttpClient(_foundry);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Norvia.Test.SimulatorFoundry",
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
            Microsoft.Extensions.Options.Options.Create(new AgentsOptions
            {
                Enabled = true,
                TimeoutSeconds = timeoutSeconds,
                Foundry = new FoundryOptions { ProjectEndpoint = new Uri("https://ais-norvia.services.ai.azure.com/api/projects/norvia") },
            }),
            credential,
            _activities,
            new FakeLogger<FoundryAgentClient>(_logs));
    }

    [Fact]
    public async Task GeneratePoolAsync_AsksThePublishedAgent_StoresNothingAndCapsTheAnswer()
    {
        _foundry.Answer(Message("""{"texts":["Geen wagen","Vol","Te laat"]}"""));

        var result = await Client().GeneratePoolAsync(Request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal("Geen wagen", "Vol", "Te laat");
        var sent = _foundry.Requests.Should().ContainSingle().Which;
        sent.Uri.Should().Be("https://ais-norvia.services.ai.azure.com/api/projects/norvia/openai/v1/responses");
        sent.Authorization.Should().Be("Bearer foundry-token");
        var body = JsonNode.Parse(sent.Body)!;
        body["agent_reference"]!["name"]!.GetValue<string>().Should().Be("simulator-personas");
        body["store"]!.GetValue<bool>().Should().BeFalse();
        body["max_output_tokens"]!.GetValue<int>().Should().Be(800);
        body["input"]![0]!["content"]!.GetValue<string>().Should().Contain("profile: terse-dutch").And.Contain("category: DeclineReason");
    }

    [Fact]
    public async Task GeneratePoolAsync_TheRunIsASpanWithAnId_AndNoTextIsOnItOrInTheLog()
    {
        _foundry.Answer(Message("""{"texts":["Geheime tekst","Vol","Te laat"]}"""));

        await Client().GeneratePoolAsync(Request, TestContext.Current.CancellationToken);

        var span = _spans.Should().ContainSingle().Which;
        span.OperationName.Should().Be("Agent run");
        span.GetTagItem("agent.name").Should().Be("simulator-personas");
        span.GetTagItem("agent.run.id").Should().BeOfType<string>().Which.Should().NotBeEmpty();
        span.GetTagItem("persona.profile").Should().Be("terse-dutch");
        span.Tags.Select(tag => tag.Value).Should().NotContain(value => value != null && value.Contains("Geheime", StringComparison.Ordinal));
        _logs.GetSnapshot().Select(record => record.Message).Should().NotContain(message => message.Contains("Geheime", StringComparison.Ordinal));
        _logs.GetSnapshot().Should().ContainSingle(record => record.Message.Contains("wrote a pool for terse-dutch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GeneratePoolAsync_FoundryRefuses_IsUnavailable_AndMarksTheSpan()
    {
        _foundry.Fail(HttpStatusCode.TooManyRequests);

        var result = await Client().GeneratePoolAsync(Request, TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Unavailable);
        _spans.Should().ContainSingle().Which.Status.Should().Be(ActivityStatusCode.Error);
        _foundry.Requests.Should().ContainSingle("there is no retry");
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("incomplete")]
    [InlineData("cancelled")]
    public async Task GeneratePoolAsync_AResponseThatDidNotComplete_IsUnavailable(string status)
    {
        _foundry.Answer(Message("""{"texts":["a","b","c"]}"""), status);

        var result = await Client().GeneratePoolAsync(Request, TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Unavailable);
    }

    [Fact]
    public async Task GeneratePoolAsync_AnAnswerThatIsNotAListOfTexts_IsUnavailable()
    {
        _foundry.Answer(Message("Sorry, I can't."));

        var result = await Client().GeneratePoolAsync(Request, TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Unavailable);
    }

    [Fact]
    public async Task GeneratePoolAsync_AToolCall_IsRefused_ForTheAgentHasNoTool()
    {
        _foundry.Answer(new JsonObject { ["type"] = "mcp_call", ["name"] = "anything" }, Message("""{"texts":["a","b","c"]}"""));

        var result = await Client().GeneratePoolAsync(Request, TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task GeneratePoolAsync_PastTheTimeout_IsUnavailable()
    {
        _foundry.Hang = true;

        var result = await Client(timeoutSeconds: 1).GeneratePoolAsync(Request, TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Unavailable);
    }

    [Fact]
    public async Task GeneratePoolAsync_TheCallerCancelling_IsNotSwallowed()
    {
        _foundry.Hang = true;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => Client().GeneratePoolAsync(Request, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static JsonObject Message(string text) => new()
    {
        ["type"] = "message",
        ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }),
    };

    private sealed record Recorded(string Uri, string? Authorization, string Body);

    private sealed class FakeResponses : HttpMessageHandler
    {
        private JsonObject[] _output = [];
        private string _status = "completed";
        private HttpStatusCode? _failure;

        public List<Recorded> Requests { get; } = [];

        public bool Hang { get; set; }

        public void Answer(JsonObject output, string status = "completed") => Answer([output], status);

        public void Answer(JsonObject first, JsonObject second) => Answer([first, second], "completed");

        public void Fail(HttpStatusCode status) => _failure = status;

        private void Answer(JsonObject[] output, string status) => (_output, _status) = (output, status);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Recorded(request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body));
            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (_failure is { } failure)
            {
                return new HttpResponseMessage(failure);
            }

            var response = new JsonObject { ["id"] = "resp_1", ["status"] = _status, ["output"] = new JsonArray([.. _output.Select(item => (JsonNode)item.DeepClone())]) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
