using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using static Norvia.Integration.BookingDraft.Api.IntegrationTests.FakeResponses;

namespace Norvia.Integration.BookingDraft.Api.IntegrationTests;

/// <summary>
/// The operation end to end through the real host (ADR 0040): what API Management sends in, who may call, what the host asks Foundry and what it answers.
/// Each test builds a host of its own with its own key, its own faked project and its own log, so none depends on another.
/// </summary>
public sealed class GatewayCallTests
{
    private const string Text = "SECRET-CONSIGNEE-TEXT";
    private const string Draft = """{"booking": {}, "quotes": {}, "missing": []}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpRequestMessage Request(string? token, string body = $$"""{"text": "{{Text}}"}""", string? correlationId = "corr-1", string? shipper = "SHIPPER-1")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AzureHost.Route) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        if (shipper is not null)
        {
            request.Headers.Add("X-Shipper-ID", shipper);
        }

        return request;
    }

    private static Dictionary<string, object?> Keys(Microsoft.Extensions.Logging.Testing.FakeLogRecord record) =>
        record.Scopes.SelectMany(scope => (IEnumerable<KeyValuePair<string, object?>>)scope!).GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Last().Value);

    // The gateway's call

    [Fact]
    public async Task TheGateway_GetsTheAgentsDraft_WithItsCorrelationId()
    {
        await using var host = new AzureHost();
        host.Responses.Answer(Message(Draft));

        using var response = await host.Client.SendAsync(Request(host.Token()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Correlation-ID").Should().Equal("corr-1");
        (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!.ContainsKey("booking").Should().BeTrue();
    }

    [Fact]
    public async Task TheHost_AsksThePublishedAgentOfTheIntakeProject_AsItsOwnIdentity_StoringNothing()
    {
        await using var host = new AzureHost();
        host.Responses.Answer(Message(Draft));

        using var _ = await host.Client.SendAsync(Request(host.Token()), Ct);

        var create = host.Responses.Requests.Single(request => request.Method == HttpMethod.Post);
        create.Uri.Should().Be($"{AzureHost.ProjectEndpoint}/openai/v1/responses");
        create.Authorization.Should().Be("Bearer foundry-token", "the Foundry call is the host's own, never the Shipper's or the gateway's token");
        var body = JsonNode.Parse(create.Body)!;
        body["agent_reference"]!["name"]!.GetValue<string>().Should().Be("booking-intake");
        body["store"]!.GetValue<bool>().Should().BeFalse();
        body["conversation"].Should().BeNull();
        body["tools"].Should().BeNull();
        body["input"]!.ToJsonString().Should().Contain(Text);
    }

    [Fact]
    public async Task TheHost_DeletesTheResponse_BeforeTheShipperGetsTheDraft()
    {
        await using var host = new AzureHost();
        host.Responses.Answer(Message(Draft));

        using var _ = await host.Client.SendAsync(Request(host.Token()), Ct);

        host.Responses.Requests.Select(request => request.Method).Should().Equal(HttpMethod.Post, HttpMethod.Delete);
        host.Responses.Requests[1].Uri.Should().EndWith("/openai/v1/responses/resp-1");
    }

    [Fact]
    public async Task TheHost_WhenTheResponseCannotBeDeleted_StillAnswersTheDraft()
    {
        await using var host = new AzureHost();
        host.Responses.Answer(Message(Draft));
        host.Responses.DeleteStatus = HttpStatusCode.InternalServerError;

        using var response = await host.Client.SendAsync(Request(host.Token()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Spans.Should().Contain(span => span.GetTagItem("failureCode.cleanup") as string == "ConversationNotDeleted");
    }

    // Only API Management may call the host

    [Fact]
    public async Task ACallWithoutAToken_IsRefused401_AndReachesNoAgent()
    {
        await using var host = new AzureHost();

        using var response = await host.Client.SendAsync(Request(token: null), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Responses.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ATokenSignedByAnotherKey_IsRefused401_AndReachesNoAgent()
    {
        await using var host = new AzureHost();

        using var response = await host.Client.SendAsync(Request(host.Token(signingKey: new byte[64])), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Responses.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ATokenForAnotherAudience_IsRefused401_AndReachesNoAgent()
    {
        await using var host = new AzureHost();

        using var response = await host.Client.SendAsync(Request(host.Token(audience: "api://norvia-partner-api")), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Responses.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ATokenWithoutTheHostsRole_IsRefused403_AndReachesNoAgent()
    {
        await using var host = new AzureHost();

        using var response = await host.Client.SendAsync(Request(host.Token(roles: ["TransportOrders.Draft"])), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Responses.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ATokenWithTheRole_FromAClientThatIsNotTheGateway_IsRefused403_AndReachesNoAgent()
    {
        await using var host = new AzureHost();

        using var response = await host.Client.SendAsync(Request(host.Token(azp: "00000000-0000-0000-0000-0000000000ff")), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Responses.Requests.Should().BeEmpty();
    }

    // What the host answers

    [Fact]
    public async Task ARequestWithoutAText_IsRefused400_WithItsCode()
    {
        await using var host = new AzureHost();

        using var response = await host.Client.SendAsync(Request(host.Token(), body: "{}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["failureCode"]!.GetValue<string>().Should().Be("TextRequired");
        host.Responses.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ATextOverTheLimit_IsRefused413_AndReachesNoAgent()
    {
        await using var host = new AzureHost(maxCharacters: 100);

        using var response = await host.Client.SendAsync(Request(host.Token(), body: $$"""{"text": "{{new string('x', 101)}}"}"""), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        host.Responses.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ASwitchedOffAssistant_AnswersUnavailable503_AndAsksNoAgent()
    {
        await using var host = new AzureHost(enabled: false);

        using var response = await host.Client.SendAsync(Request(host.Token()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["failureCode"]!.GetValue<string>().Should().Be("AgentsDisabled");
        host.Responses.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AProjectThatAnswersWithAnError_IsUnavailable503_AndAFailedHop()
    {
        await using var host = new AzureHost();
        host.Responses.Fail(HttpStatusCode.InternalServerError);

        using var response = await host.Client.SendAsync(Request(host.Token()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        host.Logs.GetSnapshot().Should().Contain(record => record.Message.StartsWith("The assistant failed", StringComparison.Ordinal) && (string?)Keys(record)["outcome"] == "Failed");
    }

    [Fact]
    public async Task AProjectThatDoesNotAnswerInTime_IsUnavailable503()
    {
        await using var host = new AzureHost(timeoutSeconds: 1);
        host.Responses.Hang = true;

        using var response = await host.Client.SendAsync(Request(host.Token()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task AnAgentThatCallsATool_IsRefused_AsItsDefinitionHasNone()
    {
        await using var host = new AzureHost();
        host.Responses.Answer(new JsonObject { ["type"] = "mcp_call", ["id"] = "mcp_1", ["name"] = "anything" }, Message(Draft));

        using var response = await host.Client.SendAsync(Request(host.Token()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!.ToJsonString().Should().NotContain(Text);
    }

    // The trail

    [Fact]
    public async Task TheTrail_HoldsTheKeysOfTheOperationsHop()
    {
        await using var host = new AzureHost();
        host.Responses.Answer(Message(Draft));

        using var _ = await host.Client.SendAsync(Request(host.Token()), Ct);

        var answered = Keys(host.Logs.GetSnapshot().Single(record => record.Message == "Answered with a Booking draft"));
        answered["correlationId"].Should().Be("corr-1");
        answered["flow"].Should().Be("booking-draft");
        answered["partyId"].Should().Be("SHIPPER-1");
        answered["outcome"].Should().Be("Succeeded");
    }

    [Fact]
    public async Task TheTrail_HoldsTheKeysOfTheAgentsHop()
    {
        await using var host = new AzureHost();
        host.Responses.Answer(Message(Draft));

        using var _ = await host.Client.SendAsync(Request(host.Token()), Ct);

        var agent = Keys(host.Logs.GetSnapshot().Single(record => record.Message == "The Intake agent answered"));
        agent["agentId"].Should().Be("booking-intake");
        agent["correlationId"].Should().Be("corr-1");
        agent["runId"].Should().NotBeNull();
    }

    [Fact]
    public async Task TheTrail_NeverHoldsTheTextTheDraftOrATokenInALog()
    {
        await using var host = new AzureHost();
        host.Responses.Answer(Message(Draft));
        var token = host.Token();

        using var _ = await host.Client.SendAsync(Request(token), Ct);

        var logs = host.Logs.GetSnapshot();
        var everything = string.Concat(logs.Select(record => record.Message + string.Join(',', Keys(record).Values) + record.Exception));
        everything.Should().NotContain(Text).And.NotContain(token).And.NotContain("foundry-token").And.NotContain("quotes");
    }

    [Fact]
    public async Task TheTrail_NeverHoldsTheTextOnASpan()
    {
        await using var host = new AzureHost();
        host.Responses.Answer(Message(Draft));

        using var _ = await host.Client.SendAsync(Request(host.Token()), Ct);

        host.Spans.Should().NotBeEmpty();
        host.Spans.SelectMany(span => span.TagObjects).Select(tag => $"{tag.Value}").Should().NotContain(value => value.Contains(Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATokenForTheHostsAudience_FromAClientThatIsNotTheGateway_EvenWithTheDraftRoleOfThePartnerApi_IsRefused403()
    {
        // A Shipper's client holds TransportOrders.Draft for the Partner API; here the audience is right, so only the host's role and azp stop it.
        await using var host = new AzureHost();

        using var response = await host.Client.SendAsync(Request(host.Token(azp: "00000000-0000-0000-0000-0000000000ff", roles: ["TransportOrders.Draft"])), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Responses.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("incomplete")]
    [InlineData("cancelled")]
    public async Task AResponseThatDidNotComplete_IsUnavailable503_AndStillDeleted(string status)
    {
        await using var host = new AzureHost();
        host.Responses.Answer(status, Message(Draft));

        using var response = await host.Client.SendAsync(Request(host.Token()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        host.Responses.Requests.Select(request => request.Method).Should().Equal(HttpMethod.Post, HttpMethod.Delete);
    }

    [Fact]
    public async Task AnAnswerThatIsNoJson_IsUnavailable503_WithoutTheAnswerInTheBody()
    {
        await using var host = new AzureHost();
        host.Responses.Answer(Message("I could not read that email, sorry."));

        using var response = await host.Client.SendAsync(Request(host.Token()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync(Ct)).Should().NotContain("sorry");
    }
}
