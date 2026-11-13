using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Application.TrackingAssistant;
using Norvia.PartnerPortal.Infrastructure.Assistant;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Assistant;

public sealed class FoundryAgentClientTests
{
    private const string Reply = """{"answer":"It is estimated to arrive 02-10-2030 11:30.","claims":[{"field":"detail.eta"}],"noInformation":false}""";

    private static readonly ShipperTrackingDetail Detail = new(
        "TO-0000042", "BD-1", ShipperTrackingStatus.InTransit, new ShipperPlace("Rotterdam", "NL"), "01-10-2030 08:00", "01-10-2030 12:00",
        new ShipperPlace("Utrecht", "NL"), "02-10-2030 09:00", "02-10-2030 13:00", Eta: "02-10-2030 11:30", LastUpdate: "01-10-2030 14:05", Progress: [], ProofOfDelivery: null);

    private static AssistantRequest Request() =>
        new("run-1", AssistantInputBuilder.Build("Where is my order?", [], new TrackingData(Detail, [], null), new DateTimeOffset(2030, 10, 1, 12, 0, 0, TimeSpan.Zero), new AssistantLimits()), 400);

    private static string Response(string text = Reply, int totalTokens = 1234, string status = "completed", params JsonNode[] extraOutput)
    {
        var output = new JsonArray(new JsonObject
        {
            ["type"] = "message",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }),
        });
        foreach (var item in extraOutput)
        {
            output.Add(item);
        }

        return new JsonObject { ["id"] = "resp_1", ["status"] = status, ["output"] = output, ["usage"] = new JsonObject { ["total_tokens"] = totalTokens } }.ToJsonString();
    }

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri? Uri, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.RequestUri, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (FoundryAgentClient Client, FakeHandler Handler) Client(HttpStatusCode status = HttpStatusCode.OK, string? body = null)
    {
        var handler = new FakeHandler(status, body ?? Response());
        var credential = Substitute.For<TokenCredential>();
        credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>()).Returns(new AccessToken("token", DateTimeOffset.UtcNow.AddHours(1)));
        var options = Options.Create(new AssistantOptions { Enabled = true, Foundry = { ProjectEndpoint = new Uri("https://ais.services.ai.azure.com/api/projects/norvia-intake") } });
        return (new FoundryAgentClient(new HttpClient(handler), options, credential, NullLogger<FoundryAgentClient>.Instance), handler);
    }

    [Fact]
    public async Task Ask_StoresNothing_StartsNoConversation_AndCapsTheAnswer()
    {
        var (client, handler) = Client();

        var result = await client.AskAsync(Request(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var call = handler.Calls.Should().ContainSingle("nothing is deleted or fetched afterwards: the response was never stored").Subject;
        call.Method.Should().Be(HttpMethod.Post);
        call.Uri!.AbsoluteUri.Should().Be("https://ais.services.ai.azure.com/api/projects/norvia-intake/openai/v1/responses");
        var body = JsonNode.Parse(call.Body)!.AsObject();
        body["store"]!.GetValue<bool>().Should().BeFalse();
        body["agent_reference"]!["name"]!.GetValue<string>().Should().Be("shipper-tracking-assistant");
        body["max_output_tokens"]!.GetValue<int>().Should().Be(400);
        body.ContainsKey("conversation").Should().BeFalse();
        body.ContainsKey("tools").Should().BeFalse();
        body["input"]![0]!["content"]!.GetValue<string>().Should().Be(Request().Input.Text);
    }

    [Fact]
    public async Task Ask_ReturnsTheAnswerItsClaimsAndTheUsageFoundryReturned()
    {
        var (client, _) = Client(body: Response(totalTokens: 777));

        var result = await client.AskAsync(Request(), TestContext.Current.CancellationToken);

        result.Value.Answer.Should().Contain("02-10-2030 11:30");
        result.Value.Claims.Select(claim => claim.Field).Should().Equal("detail.eta");
        result.Value.TokensUsed.Should().Be(777);
    }

    [Fact]
    public async Task Ask_WhenFoundryReportsNoUsage_CountsNothing()
    {
        var (client, _) = Client(body: new JsonObject { ["status"] = "completed", ["output"] = JsonNode.Parse(JsonNode.Parse(Response())!["output"]!.ToJsonString()) }.ToJsonString());

        var result = await client.AskAsync(Request(), TestContext.Current.CancellationToken);

        result.Value.TokensUsed.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, null)]
    [InlineData(HttpStatusCode.TooManyRequests, null)]
    [InlineData(HttpStatusCode.OK, "{\"status\":\"failed\",\"output\":[]}")]
    [InlineData(HttpStatusCode.OK, "{\"status\":\"incomplete\",\"output\":[]}")]
    [InlineData(HttpStatusCode.OK, "{\"error\":{\"code\":\"x\"},\"output\":[]}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task Ask_WhenFoundryFails_IsAgentUnavailable(HttpStatusCode status, string? body)
    {
        var (client, _) = Client(status, body ?? "{}");

        var result = await client.AskAsync(Request(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().ContainSingle().Which.Should().Be("AgentUnavailable");
    }

    [Fact]
    public async Task Ask_WhenTheAgentCalledATool_IsAgentUnavailable()
    {
        var (client, _) = Client(body: Response(extraOutput: new JsonObject { ["type"] = "mcp_call", ["name"] = "get_orders" }));

        var result = await client.AskAsync(Request(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Unavailable);
    }

    [Fact]
    public async Task Ask_WhenTheReplyIsNotTheAssistantsResult_IsAgentUnavailable()
    {
        var (client, _) = Client(body: Response("sure, it arrives tomorrow"));

        var result = await client.AskAsync(Request(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Unavailable);
    }

    [Fact]
    public async Task Ask_WhenTheShipperCancels_Throws()
    {
        var (client, _) = Client();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => client.AskAsync(Request(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
