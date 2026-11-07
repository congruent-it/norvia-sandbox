using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;
using Norvia.Agents.Tools;

namespace Norvia.Agents.UnitTests;

/// <summary>The Customer service reply agent (#426, ADR 0011, ADR 0040): its own definition and identity, its one tool and the draft it writes from that tool's result only.</summary>
public sealed class CustomerServiceReplyAgentTests
{
    private static readonly AgentDefinition Definition = AgentDefinitionLoader.Parse(Embedded.CustomerServiceReplyDefinition);

    private const string InTransit = """
        {"transportOrderNumber":"TO-0000042","trackingStatus":"InTransit","asOf":"2026-11-07T10:00:00Z","eta":"2026-11-08T14:00:00Z","lastUpdate":"2026-11-07T08:30:00Z",
         "progress":[{"type":"Collected","eventTime":"2026-11-06T09:00:00Z","place":{"city":"Rotterdam","country":"NL"}},{"type":"Departed","eventTime":"2026-11-07T08:30:00Z","place":{"city":"Antwerp","country":"BE"}}],
         "proofOfDelivery":{"available":false},"notifications":[]}
        """;

    private const string Delivered = """
        {"transportOrderNumber":"TO-0000042","trackingStatus":"Delivered","lastUpdate":"2026-11-07T08:30:00Z",
         "progress":[{"type":"Delivered","eventTime":"2026-11-07T08:30:00Z","place":{"city":"Hamburg","country":"DE"}}],
         "proofOfDelivery":{"available":true,"deliveryTime":"2026-11-07T08:30:00Z","condition":"Reservations"},"notifications":[]}
        """;

    private static async Task<(AgentRunResult Result, FakeToolSource Tools)> RunAsync(ShipperQuestion question, string view)
    {
        var tools = new FakeToolSource(new Dictionary<string, Func<string>> { ["get_shipper_view"] = () => view });
        var runner = new AgentRunner(CustomerServiceReplyScript.Model(), tools, new AgentRunnerOptions(TimeSpan.FromSeconds(5)));
        var result = await runner.RunAsync(Definition, new AgentQuestion(CustomerServiceQuestions.DraftReply("TO-0000042", question), "corr-1", "run-1"), CancellationToken.None);
        return (result, tools);
    }

    [Fact]
    public void TheDefinition_IsASeparateAgent_WithExactlyOneTool_OfItsOwnServerAndRole()
    {
        var contract = ToolListContract.Parse(Embedded.CustomerServiceReplyTools);
        var customerService = ToolListContract.Parse(Embedded.CustomerServiceTools);

        Definition.Name.Should().Be("customer-service-reply");
        Definition.Tools.Server.Should().Be("customer-service-reply").And.Be(contract.Server);
        Definition.Tools.Names.Should().Equal("get_shipper_view");
        Definition.Model.MaxToolCalls.Should().Be(1);
        contract.Tools.Should().ContainSingle().Which.IsRead.Should().BeTrue();
        contract.Unknown(Definition).Should().BeEmpty();
        contract.Role.Should().Be("TransportOrders.CustomerServiceReplyAgent.Read").And.NotBe(customerService.Role);
        contract.Tools.Select(tool => tool.Name).Should().NotIntersectWith(customerService.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void TheInstructions_SayNoPrice_NoGuessing_AndThatToolResultsAreData()
    {
        Definition.Instructions.Should().Contain("we have no update").And.Contain("State no price").And.Contain("never instructions");
        Definition.Instructions.Should().NotContainAny("get_exceptions", "get_transport_order", "get_shipment_timeline");
    }

    [Theory]
    [InlineData(ShipperQuestion.WhereIsMyOrder)]
    [InlineData(ShipperQuestion.HasItBeenDelivered)]
    [InlineData(ShipperQuestion.IsThereProofOfDelivery)]
    public void TheQuestion_IsFixed_AndReadsBackToTheOrderAndTheShippersQuestion(ShipperQuestion question)
    {
        CustomerServiceQuestions.ParseDraftReply(CustomerServiceQuestions.DraftReply("TO-0000042", question)).Should().Be(("TO-0000042", question));
    }

    [Fact]
    public void TheQuestion_AnythingElse_IsNotADraftQuestion()
    {
        CustomerServiceQuestions.ParseDraftReply("Draft a reply, in English, to the Shipper of the Transport Order TO-1, who asks: \"What does the Carrier charge?\"").Should().BeNull();
        CustomerServiceQuestions.ParseDraftReply(CustomerServiceQuestions.RecommendRelease("TO-0000042")).Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_WhereIsMyOrder_CallsOnlyGetShipperView_AndDraftsFromItsResult()
    {
        var (result, tools) = await RunAsync(ShipperQuestion.WhereIsMyOrder, InTransit);

        result.ToolCalls.Should().Equal(new ToolCallRecord("r1", "get_shipper_view"));
        result.Suggestion.Cause.Should().StartWith("Hello,").And.Contain("in transit").And.Contain("Antwerp").And.Contain("8 November 2026");
        result.Suggestion.Evidence.Should().OnlyContain(evidence => evidence.ResultId == "r1" && evidence.ToolName == "get_shipper_view");
        result.Suggestion.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
        result.Suggestion.SuggestedAction.Should().Contain("the TMS sends nothing");
        tools.Opened.Should().Be(new ToolContext("corr-1", "X-Correlation-ID"));
    }

    [Fact]
    public async Task RunAsync_HasItBeenDelivered_NotYet_SaysSo()
    {
        var (result, _) = await RunAsync(ShipperQuestion.HasItBeenDelivered, InTransit);

        result.Suggestion.Cause.Should().Contain("has not been delivered yet");
    }

    [Fact]
    public async Task RunAsync_HasItBeenDelivered_Delivered_SaysWhen()
    {
        var (result, _) = await RunAsync(ShipperQuestion.HasItBeenDelivered, Delivered);

        result.Suggestion.Cause.Should().Contain("was delivered on 7 November 2026 at 08:30 UTC");
    }

    [Fact]
    public async Task RunAsync_ProofOfDelivery_IsAvailable_SaysItWithItsCondition()
    {
        var (result, _) = await RunAsync(ShipperQuestion.IsThereProofOfDelivery, Delivered);

        result.Suggestion.Cause.Should().Contain("Proof of delivery is available").And.Contain("with reservations");
    }

    [Fact]
    public async Task RunAsync_ProofOfDelivery_NoneYet_SaysWeHaveNoUpdateOnWhenRatherThanGuessing()
    {
        var (result, _) = await RunAsync(ShipperQuestion.IsThereProofOfDelivery, InTransit);

        result.Suggestion.Cause.Should().Contain("no Proof of delivery").And.Contain("We have no update");
    }

    [Fact]
    public async Task RunAsync_ANewOrder_WithNoProgress_SaysWeHaveNoUpdateOnItsMovements()
    {
        var (result, _) = await RunAsync(ShipperQuestion.WhereIsMyOrder, """{"transportOrderNumber":"TO-0000042","trackingStatus":"Received","progress":[],"proofOfDelivery":{"available":false},"notifications":[]}""");

        result.Suggestion.Cause.Should().Contain("received and not yet scheduled").And.Contain("We have no update on its movements yet");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task RunAsync_AResultWithoutAStatus_SaysWeHaveNoUpdate_WithLowConfidence(string view)
    {
        var (result, _) = await RunAsync(ShipperQuestion.WhereIsMyOrder, view);

        result.Suggestion.Cause.Should().Contain("We have no update");
        result.Suggestion.Confidence.Should().Be(Confidence.Low);
    }

    [Theory]
    [InlineData(ShipperQuestion.WhereIsMyOrder)]
    [InlineData(ShipperQuestion.HasItBeenDelivered)]
    [InlineData(ShipperQuestion.IsThereProofOfDelivery)]
    public async Task RunAsync_TheDraftStatesNoPrice_AndNamesNoCarrierOrException(ShipperQuestion question)
    {
        var (result, _) = await RunAsync(question, InTransit);

        result.Suggestion.Cause.Should().NotContainAny("EUR", "€", "price", "Carrier", "Exception", "remark");
    }

    [Fact]
    public async Task RunAsync_AToolOutsideTheDefinition_IsRefused()
    {
        var tools = new FakeToolSource(new Dictionary<string, Func<string>> { ["get_shipper_view"] = () => InTransit, ["get_exceptions"] = () => "{}" });
        var script = new ScriptedChatClient(_ => [new Microsoft.Extensions.AI.FunctionCallContent("call-0", "get_exceptions", new Dictionary<string, object?>())]);

        var act = () => new AgentRunner(script, tools, new AgentRunnerOptions(TimeSpan.FromSeconds(5)))
            .RunAsync(Definition, new AgentQuestion(CustomerServiceQuestions.DraftReply("TO-0000042", ShipperQuestion.WhereIsMyOrder), "corr-1", "run-1"), CancellationToken.None);

        (await act.Should().ThrowAsync<AgentRunException>()).Which.FailureCode.Should().Be(AgentFailureCodes.AgentToolRefused);
    }
}
