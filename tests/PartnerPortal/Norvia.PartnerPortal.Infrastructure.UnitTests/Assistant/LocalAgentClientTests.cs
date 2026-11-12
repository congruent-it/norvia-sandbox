using Ardalis.Result;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Norvia.Agents.Scripts;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Application.TrackingAssistant;
using Norvia.PartnerPortal.Infrastructure.Assistant;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Assistant;

public sealed class LocalAgentClientTests
{
    private static readonly ShipperTrackingDetail Detail = new(
        "TO-0000042", "BD-1", ShipperTrackingStatus.InTransit, new ShipperPlace("Rotterdam", "NL"), "01-10-2030 08:00", "01-10-2030 12:00",
        new ShipperPlace("Utrecht", "NL"), "02-10-2030 09:00", "02-10-2030 13:00", Eta: "02-10-2030 11:30", LastUpdate: "01-10-2030 14:05", Progress: [], ProofOfDelivery: null);

    private static AssistantRequest Request(string question) =>
        new("run-1", AssistantInputBuilder.Build(question, [], new TrackingData(Detail, [], null), new DateTimeOffset(2030, 10, 1, 12, 0, 0, TimeSpan.Zero)), 400);

    private static LocalAgentClient Client(IChatClient? model = null) => new(Options.Create(new AssistantOptions { Enabled = true }), NullLogger<LocalAgentClient>.Instance, model);

    [Fact]
    public async Task Ask_ReturnsTheScriptedAnswerWithItsClaimsAndTokens_ThatPassTheOutputCheck()
    {
        var request = Request("Where is my order?");

        var result = await Client().AskAsync(request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Answer.Should().Contain("02-10-2030 11:30");
        result.Value.Claims.Select(claim => claim.Field).Should().Contain("detail.eta");
        result.Value.TokensUsed.Should().BeGreaterThan(0);
        AnswerCheck.Passes(result.Value, request.Input).Should().BeTrue();
    }

    [Fact]
    public async Task Ask_WhenTheModelBreaks_IsAgentUnavailable()
    {
        var model = Substitute.For<IChatClient>();
        model.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>()).Returns<ChatResponse>(_ => throw new InvalidOperationException("boom"));

        var result = await Client(model).AskAsync(Request("Where is my order?"), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().ContainSingle().Which.Should().Be("AgentUnavailable");
    }

    [Fact]
    public async Task Ask_WhenTheModelAnswersSomethingElse_IsAgentUnavailable()
    {
        var model = new ScriptedChatClient(_ => [new TextContent("sure, it arrives tomorrow")]);

        var result = await Client(model).AskAsync(Request("Where is my order?"), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Unavailable);
    }

    [Fact]
    public async Task Ask_WhileTheAssistantIsOff_IsAgentUnavailable()
    {
        var result = await new DisabledAgentClient().AskAsync(Request("Where is my order?"), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Unavailable);
    }

    [Fact]
    public void Switch_FollowsTheOption()
    {
        new ConfiguredAssistantSwitch(Options.Create(new AssistantOptions { Enabled = true })).Enabled.Should().BeTrue();
        new ConfiguredAssistantSwitch(Options.Create(new AssistantOptions())).Enabled.Should().BeFalse();
        new GuidAssistantRunIds().NewRunId().Should().NotBe(new GuidAssistantRunIds().NewRunId());
    }
}
