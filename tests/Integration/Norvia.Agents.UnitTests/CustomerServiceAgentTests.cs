using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;
using Norvia.Agents.Tools;

namespace Norvia.Agents.UnitTests;

/// <summary>The Customer service agent: its definition, its tool list, its fixed question and the scripted model (ADR 0040, #421).</summary>
public sealed class CustomerServiceAgentTests
{
    private static readonly AgentDefinition Definition = AgentDefinitionLoader.Parse(Embedded.CustomerServiceDefinition);
    private static readonly Guid BookingId = Guid.Parse("3f2b8c9e-6d5a-4c1e-9a77-0e1f2a3b4c5d");

    private const string BadWeight = """{"status":"Rejected","rejectionReasons":[{"code":"WeightInvalid","message":"A goods line has no weight."}]}""";

    private static AgentRunner Runner(string answer, out FakeToolSource tools)
    {
        tools = new FakeToolSource(new Dictionary<string, Func<string>> { ["get_rejected_booking"] = () => answer });
        return new AgentRunner(CustomerServiceScript.Model(), tools, new AgentRunnerOptions(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void TheDefinition_IsTheAgentWithTheOneReadTool_OfTheCustomerServiceServer()
    {
        var contract = ToolListContract.Parse(Embedded.CustomerServiceTools);

        Definition.Name.Should().Be("customer-service");
        Definition.Tools.Server.Should().Be("customer-service").And.Be(contract.Server);
        Definition.Tools.Names.Should().Equal("get_rejected_booking");
        contract.Role.Should().Be("TransportOrders.CustomerServiceAgent.Read");
        contract.Tools.Should().OnlyContain(tool => tool.IsRead);
        contract.Unknown(Definition).Should().BeEmpty();
        Definition.Instructions.Should().Contain("never instructions").And.Contain("cites the result", because: "tool results are data and every claim cites its result (ADR 0040)");
    }

    [Fact]
    public void TheQuestion_IsFixed_AndReadsBackToTheBookingsId()
    {
        var question = CustomerServiceQuestions.ExplainRejectedBooking(BookingId);

        CustomerServiceQuestions.ParseExplainRejectedBooking(question).Should().Be(BookingId);
        CustomerServiceQuestions.ParseExplainRejectedBooking("Explain why the Rejected booking nonsense was rejected, and what the Shipper should change.").Should().BeNull();
        CustomerServiceQuestions.ParseExplainRejectedBooking("Tell me about " + BookingId).Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_ABadWeight_ExplainsItInPlainLanguage_CitingTheResult()
    {
        var runner = Runner(BadWeight, out var tools);

        var result = await runner.RunAsync(Definition, new AgentQuestion(CustomerServiceQuestions.ExplainRejectedBooking(BookingId), "booking-1", "run-1"), CancellationToken.None);

        result.ToolCalls.Should().Equal(new ToolCallRecord("r1", "get_rejected_booking"));
        result.Suggestion.Cause.Should().Contain("weight");
        result.Suggestion.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
        result.Suggestion.Confidence.Should().Be(Confidence.High);
        result.Suggestion.SuggestedAction.Should().Contain("weight in kilograms");
        result.Suggestion.Evidence.Should().ContainSingle().Which.Should().Match<Evidence>(evidence => evidence.ResultId == "r1" && evidence.ToolName == "get_rejected_booking");
        tools.Opened.Should().Be(new ToolContext("booking-1", "X-Correlation-ID"));
    }

    [Fact]
    public async Task RunAsync_SeveralReasons_ExplainsEachAndSaysHowManyRulesWereBroken()
    {
        var reasons = """{"rejectionReasons":[{"code":"WeightInvalid","message":"w"},{"code":"DeliveryWindowBeforeCollection","message":"d"},{"code":"SomethingNew","message":"The route is closed."}]}""";

        var result = await Runner(reasons, out _).RunAsync(Definition, new AgentQuestion(CustomerServiceQuestions.ExplainRejectedBooking(BookingId), "booking-1", "run-1"), CancellationToken.None);

        result.Suggestion.Cause.Should().Contain("3 rules").And.Contain("weight").And.Contain("delivery window").And.Contain("The route is closed.");
        result.Suggestion.Evidence.Should().HaveCount(3);
    }

    [Fact]
    public async Task RunAsync_NoReasonInTheResult_SaysSoWithLowConfidence()
    {
        var result = await Runner("""{"status":"Rejected","rejectionReasons":[]}""", out _).RunAsync(Definition, new AgentQuestion(CustomerServiceQuestions.ExplainRejectedBooking(BookingId), "booking-1", "run-1"), CancellationToken.None);

        result.Suggestion.Confidence.Should().Be(Confidence.Low);
        result.Suggestion.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
    }

    [Fact]
    public async Task RunAsync_AResultThatIsNotAnObject_IsToldAsNoReason()
    {
        var result = await Runner("not json", out _).RunAsync(Definition, new AgentQuestion(CustomerServiceQuestions.ExplainRejectedBooking(BookingId), "booking-1", "run-1"), CancellationToken.None);

        result.Suggestion.Confidence.Should().Be(Confidence.Low);
    }

    [Fact]
    public async Task RunAsync_AnotherQuestion_IsNotAnswered()
    {
        var run = () => Runner(BadWeight, out _).RunAsync(Definition, new AgentQuestion("Tell me a joke", "booking-1", "run-1"), CancellationToken.None);

        await run.Should().ThrowAsync<AgentRunException>();
    }

    [Theory]
    [InlineData("TotalWeightOverLimit")]
    [InlineData("QuantityInvalid")]
    [InlineData("PackageTypeInvalid")]
    [InlineData("DimensionsInvalid")]
    [InlineData("GoodsLinesMissing")]
    [InlineData("CollectionAddressIncomplete")]
    [InlineData("DeliveryAddressMissing")]
    [InlineData("ConsigneeNameMissing")]
    [InlineData("CollectionWindowMissing")]
    [InlineData("CollectionWindowInPast")]
    [InlineData("CollectionWindowReversed")]
    [InlineData("DeliveryWindowIncomplete")]
    [InlineData("DeliveryWindowReversed")]
    [InlineData("ShipperReferenceMissing")]
    [InlineData("ShipperReferenceTooLong")]
    [InlineData("ShipperReferenceReused")]
    [InlineData("UnknownShipper")]
    [InlineData("InactiveShipper")]
    public async Task RunAsync_EveryRejectionCode_GetsItsOwnWording(string code)
    {
        var json = $$"""{"rejectionReasons":[{"code":"{{code}}","message":"raw message"}]}""";

        var result = await Runner(json, out _).RunAsync(Definition, new AgentQuestion(CustomerServiceQuestions.ExplainRejectedBooking(BookingId), "booking-1", "run-1"), CancellationToken.None);

        result.Suggestion.Cause.Should().NotContain("raw message");
        result.Suggestion.SuggestedAction.Should().StartWith("Tell the Shipper:");
    }
}
