using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Norvia.Agents.Definitions;
using Norvia.Agents.Scripts;
using Norvia.PartnerPortal.Application.TrackingAssistant;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Web.IntegrationTests.TrackingAssistant;

/// <summary>
/// The Shipper tracking assistant at the Partner portal's boundary (ADR 0049, #882): the portal fetches through the real Partner API stand-in and the TMS with the asking Persona's own
/// credentials, runs the Agent in process with the scripted fake, and shows the Shipper only what its checks let through. Each test enters the Transport Orders it asks about and starts a
/// portal of its own, so none depends on another's data or counts.
/// </summary>
public sealed class TrackingAssistantTests(TrackingAssistantFixture fixture)
{
    private static readonly PersonaId Asking = new(TrackingAssistantFixture.AskingPersona);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AQuestion_IsFetchedWithTheAskingPersonasOwnTokenAndSubscriptionKey_AndNoOtherPersonas()
    {
        var order = await EnterAsync(TrackingAssistantFixture.AskingShipperId);
        await using var portal = fixture.StartPortal();

        var answer = await AskAsync(portal, "Where is my order?", order);

        answer.Outcome.Should().Be(AssistantOutcome.Answered, answer.Text);
        var (subscriptionKey, clientId) = portal.CredentialsOf(TrackingAssistantFixture.AskingPersona);
        var other = portal.CredentialsOf(TrackingAssistantFixture.OtherPersona);
        portal.Calls.Tracking.Should().NotBeEmpty();
        portal.Calls.Tracking.Should().OnlyContain(call => call.SubscriptionKey == subscriptionKey && call.ClientId == clientId && call.StatusCode == 200);
        portal.Calls.Tracking.Should().NotContain(call => call.SubscriptionKey == other.SubscriptionKey || call.ClientId == other.ClientId);
    }

    [Fact]
    public async Task APersonasInput_HoldsItsOwnShippersTransportOrdersOnly_WhileASecondShippersOrderExistsInTheData()
    {
        var own = await EnterAsync(TrackingAssistantFixture.AskingShipperId);
        var foreign = await EnterAsync(TrackingAssistantFixture.OtherShipperId);
        await using var portal = fixture.StartPortal();

        var answer = await AskAsync(portal, "Where is my order?", own);

        answer.Outcome.Should().Be(AssistantOutcome.Answered, answer.Text);
        var input = portal.Model.Calls.Should().ContainSingle().Subject.Input;
        input.Should().Contain(own.Number).And.Contain(own.Reference);
        input.Should().NotContain(foreign.Number).And.NotContain(foreign.Reference);
    }

    [Fact]
    public async Task AQuestionAboutAnotherShippersOrder_GetsItsOwnShippersDataOnly_EvenWhenTheOrderIsNamedByNumberAndByReference()
    {
        var foreign = await EnterAsync(TrackingAssistantFixture.OtherShipperId);
        await using var portal = fixture.StartPortal();

        var byNumber = await portal.Portal.AskTrackingAssistantAsync(Asking, "Where is my order?", [], foreign.Number, Ct);
        var byReference = await portal.Portal.AskTrackingAssistantAsync(Asking, $"Where is {foreign.Reference}?", [], openOrderNumber: null, Ct);

        byNumber.Outcome.Should().Be(AssistantOutcome.Answered, byNumber.Text);
        byReference.Outcome.Should().Be(AssistantOutcome.Answered, byReference.Text);
        portal.Model.Calls.Should().HaveCount(2);
        // The question is the Shipper's own text and is allowed in the input; the Transport Order's data is not.
        portal.Model.Calls.Select(call => TrackingDataOf(call.Input)).Should().OnlyContain(data => !data.Contains(foreign.Number, StringComparison.Ordinal) && !data.Contains(foreign.Reference, StringComparison.Ordinal));
        byNumber.Text.Should().NotContain(foreign.Number);
    }

    [Fact]
    public async Task TheAssistantsDefinition_HasNoTools_AndNoRunOffersOneOrCallsAnythingButTheTrackingOperations()
    {
        var order = await EnterAsync(TrackingAssistantFixture.AskingShipperId);
        await using var portal = fixture.StartPortal();

        var definition = AgentDefinitionLoader.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "shipper-tracking-assistant.agent.yaml"), Ct));
        var answer = await AskAsync(portal, "Where is my order?", order);

        definition.Tools.Names.Should().BeEmpty();
        answer.Outcome.Should().Be(AssistantOutcome.Answered, answer.Text);
        portal.Model.Calls.Should().ContainSingle().Which.ToolCount.Should().Be(0);
        // No MCP call: the portal made no call that is not a read of the Persona's Transport Orders.
        portal.Calls.All.Should().OnlyContain(call => call.Method == HttpMethod.Get && call.Uri.AbsolutePath.Contains("/transport-orders", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAnswerTheOutputCheckRejects_IsNeverShown_AndTheRunFailsWithAgentAnswerRejected()
    {
        var order = await EnterAsync(TrackingAssistantFixture.AskingShipperId);
        await using var portal = fixture.StartPortal();
        const string invented = "It will arrive tomorrow at 14:45 and costs EUR 1234.";
        portal.Model.Use(new ScriptedChatClient(_ => [new TextContent(new JsonObject
        {
            ["answer"] = $"{order.Number} is Received. {invented}",
            ["claims"] = new JsonArray(new JsonObject { ["field"] = "detail.status" }),
            ["noInformation"] = false,
        }.ToJsonString())]));

        var answer = await AskAsync(portal, "Where is my order?", order);

        answer.Outcome.Should().Be(AssistantOutcome.Failed);
        answer.FailureCode.Should().Be(AssistantFailureCodes.AgentAnswerRejected);
        answer.Text.Should().Be(AssistantTexts.NoInformation(AssistantLanguageKind.English)).And.NotContain("14:45").And.NotContain("1234");
        portal.Model.Calls.Should().ContainSingle();
        var span = fixture.RunSpan(answer.RunId!);
        span.GetTagItem("outcome").Should().Be("Failed");
        span.GetTagItem("failureCode").Should().Be(AssistantFailureCodes.AgentAnswerRejected);
    }

    [Fact]
    public async Task AFailedTrackingFetch_MakesNoModelCall_AndTheRunFailsWithTmsUnavailable()
    {
        var order = await EnterAsync(TrackingAssistantFixture.AskingShipperId);
        await using var portal = fixture.StartPortal(new Dictionary<string, string> { ["PartnerApi:BaseAddress"] = "http://127.0.0.1:1/partner/v1" });

        var answer = await AskAsync(portal, "Where is my order?", order);

        answer.Outcome.Should().Be(AssistantOutcome.Failed);
        answer.FailureCode.Should().Be(AssistantFailureCodes.TmsUnavailable);
        answer.Text.Should().Be(AssistantTexts.TrackingUnavailable(AssistantLanguageKind.English));
        portal.Model.Calls.Should().BeEmpty();
        fixture.RunSpan(answer.RunId!).GetTagItem("failureCode").Should().Be(AssistantFailureCodes.TmsUnavailable);
    }

    [Fact]
    public async Task AQuestionOverThePersonasRate_IsRefusedWithoutAModelCallOrAFetch_AsAgentRateLimited()
    {
        var order = await EnterAsync(TrackingAssistantFixture.AskingShipperId);
        await using var portal = fixture.StartPortal(new Dictionary<string, string> { ["Agents:Limits:QuestionsPerMinute"] = "1" });
        var first = await AskAsync(portal, "Where is my order?", order);
        var callsAfterFirst = portal.Calls.All.Count;

        var second = await AskAsync(portal, "And now?", order);

        first.Outcome.Should().Be(AssistantOutcome.Answered, first.Text);
        second.Outcome.Should().Be(AssistantOutcome.Refused);
        second.FailureCode.Should().Be(AssistantFailureCodes.AgentRateLimited);
        second.Text.Should().Be(AssistantTexts.RateLimited(AssistantLanguageKind.English));
        portal.Model.Calls.Should().ContainSingle();
        portal.Calls.All.Should().HaveCount(callsAfterFirst);
        var span = fixture.RunSpan(second.RunId!);
        span.GetTagItem("outcome").Should().Be("Refused");
        span.GetTagItem("failureCode").Should().Be(AssistantFailureCodes.AgentRateLimited);
    }

    [Fact]
    public async Task AQuestionOverTheSizeLimit_IsRefusedWithoutAModelCall_AsAgentRateLimited()
    {
        var order = await EnterAsync(TrackingAssistantFixture.AskingShipperId);
        await using var portal = fixture.StartPortal();

        var answer = await AskAsync(portal, new string('a', AssistantLimits.DefaultMaxQuestionCharacters + 1), order);

        answer.Outcome.Should().Be(AssistantOutcome.Refused);
        answer.FailureCode.Should().Be(AssistantFailureCodes.AgentRateLimited);
        portal.Model.Calls.Should().BeEmpty();
        portal.Calls.All.Should().BeEmpty();
    }

    [Fact]
    public async Task ARunsSpan_CarriesThePersonaAndTheFetchCorrelationIds_AndTheQuestionAndAnswerAreInNoLogOrTrackingKey()
    {
        var order = await EnterAsync(TrackingAssistantFixture.AskingShipperId);
        await using var portal = fixture.StartPortal();
        const string question = "Where is my order, quokka-7731?";

        var answer = await AskAsync(portal, question, order);

        answer.Outcome.Should().Be(AssistantOutcome.Answered, answer.Text);
        var span = fixture.RunSpan(answer.RunId!);
        span.GetTagItem("partyId").Should().Be(TrackingAssistantFixture.AskingPersona);
        span.GetTagItem("flow").Should().Be("shipper-tracking-assistant");
        var correlationIds = portal.Calls.Tracking.Select(call => call.CorrelationId).ToList();
        correlationIds.Should().NotBeEmpty().And.OnlyContain(id => !string.IsNullOrEmpty(id));
        ((string)span.GetTagItem("fetch.correlationIds")!).Split(',').Should().BeEquivalentTo(correlationIds);
        span.GetTagItem("fetch.calls").Should().Be(correlationIds.Count);

        var everythingKept = portal.Logs.Strings.Concat(fixture.Spans.SelectMany(s => s.TagObjects.Select(tag => $"{tag.Key}={tag.Value}")).Concat(span.Baggage.Select(item => $"{item.Key}={item.Value}"))).ToList();
        everythingKept.Should().NotBeEmpty();
        everythingKept.Should().NotContain(text => text.Contains("quokka-7731", StringComparison.Ordinal));
        everythingKept.Should().NotContain(text => text.Contains(answer.Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithAgentsDisabled_ThePanelIsHidden_AndARunIsRefusedWithoutAModelCall()
    {
        var order = await EnterAsync(TrackingAssistantFixture.AskingShipperId);
        await using var portal = fixture.StartPortal(new Dictionary<string, string> { ["Agents:Enabled"] = "false" });

        var answer = await AskAsync(portal, "Where is my order?", order);

        portal.Portal.TrackingAssistantEnabled.Should().BeFalse();
        answer.Outcome.Should().Be(AssistantOutcome.Failed);
        answer.FailureCode.Should().Be(AssistantFailureCodes.AgentUnavailable);
        answer.Text.Should().Be(AssistantTexts.Unavailable(AssistantLanguageKind.English));
        portal.Model.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task WithAgentsEnabled_ThePanelIsShown()
    {
        await using var portal = fixture.StartPortal();

        portal.Portal.TrackingAssistantEnabled.Should().BeTrue();
    }

    private static Task<AssistantAnswer> AskAsync(AssistantPortal portal, string question, EnteredOrder order) =>
        portal.Portal.AskTrackingAssistantAsync(Asking, question, [], order.Number, Ct);

    private async Task<EnteredOrder> EnterAsync(string shipperId)
    {
        var reference = $"ASSIST-{Guid.NewGuid():N}"[..16];
        return new EnteredOrder(await fixture.Tms.EnterTransportOrderAsync(shipperId, reference, Ct), reference);
    }

    private static string TrackingDataOf(string input)
    {
        var start = input.LastIndexOf("<tracking_data>", StringComparison.Ordinal);
        var end = input.LastIndexOf("</tracking_data>", StringComparison.Ordinal);
        return start < 0 || end < start ? string.Empty : input[start..end];
    }

    private sealed record EnteredOrder(string Number, string Reference);
}
