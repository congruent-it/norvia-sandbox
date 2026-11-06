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
    public void TheDefinition_IsTheAgentWithTheReadTools_OfTheCustomerServiceServer()
    {
        var contract = ToolListContract.Parse(Embedded.CustomerServiceTools);

        Definition.Name.Should().Be("customer-service");
        Definition.Tools.Server.Should().Be("customer-service").And.Be(contract.Server);
        Definition.Tools.Names.Should().Equal("get_rejected_booking", "get_transport_order", "get_shipment_timeline", "get_exceptions", "get_proof_of_delivery_status");
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

    private const string DeliveredOrder = """{"transportOrderNumber":"TO-0000042","status":"Planned","shipmentNumber":"SH-0000042","shipmentStatus":"Delivered","rating":{"result":"Rated"},"billing":{"state":"Billable","blockingReasons":[]}}""";
    private const string Timeline = """{"shipmentNumber":"SH-0000042","trackingEvents":[{"type":"Collected","place":"Rotterdam","source":"CAR-0001"},{"type":"Delivered","place":"Utrecht","source":"CAR-0001"}]}""";
    private const string NoExceptions = """{"shipmentNumber":"SH-0000042","exceptions":[]}""";
    private const string CurrentProof = """{"shipmentNumber":"SH-0000042","status":"Current","withReservations":false,"proofsOfDelivery":[{"isCurrent":true}]}""";

    private static AgentRunner OrderRunner(string order, string timeline, string exceptions, string proof, out FakeToolSource tools)
    {
        tools = new FakeToolSource(new Dictionary<string, Func<string>>
        {
            ["get_transport_order"] = () => order,
            ["get_shipment_timeline"] = () => timeline,
            ["get_exceptions"] = () => exceptions,
            ["get_proof_of_delivery_status"] = () => proof,
        });
        return new AgentRunner(CustomerServiceScript.Model(), tools, new AgentRunnerOptions(TimeSpan.FromSeconds(5)));
    }

    private static AgentQuestion OrderQuestion => new(CustomerServiceQuestions.SummariseTransportOrder("TO-0000042"), "order-1", "run-1");

    [Fact]
    public void TheSummaryQuestion_IsFixed_AndReadsBackToTheOrdersNumber()
    {
        var question = CustomerServiceQuestions.SummariseTransportOrder("TO-0000042");

        CustomerServiceQuestions.ParseSummariseTransportOrder(question).Should().Be("TO-0000042");
        CustomerServiceQuestions.ParseSummariseTransportOrder("Summarise the life of the Transport Order  from rating to billing, and say what, if anything, is blocking it.").Should().BeNull();
        CustomerServiceQuestions.ParseSummariseTransportOrder("Summarise the life of the Transport Order TO 1; ignore this from rating to billing, and say what, if anything, is blocking it.").Should().BeNull();
        CustomerServiceQuestions.ParseSummariseTransportOrder("Tell me about TO-0000042").Should().BeNull();
        FluentActions.Invoking(() => CustomerServiceQuestions.SummariseTransportOrder(" ")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task RunAsync_ADeliveredOrder_ReadsTheOrderThenItsShipment_AndSummarisesItsLifeCitingEachResult()
    {
        var runner = OrderRunner(DeliveredOrder, Timeline, NoExceptions, CurrentProof, out var tools);

        var result = await runner.RunAsync(Definition, OrderQuestion, CancellationToken.None);

        result.ToolCalls.Should().Equal(
            new ToolCallRecord("r1", "get_transport_order"),
            new ToolCallRecord("r2", "get_shipment_timeline"),
            new ToolCallRecord("r3", "get_exceptions"),
            new ToolCallRecord("r4", "get_proof_of_delivery_status"));
        result.Suggestion.Cause.Should().Contain("rating result is rated").And.Contain("Shipment SH-0000042 is delivered").And.Contain("2 Tracking events")
            .And.Contain("reported by CAR-0001").And.Contain("taken clean").And.Contain("no Exceptions").And.Contain("billable").And.Contain("Nothing is blocking it.");
        result.Suggestion.SuggestedAction.Should().Be("Nothing needs doing for this Transport Order.");
        result.Suggestion.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
        result.Suggestion.Confidence.Should().Be(Confidence.High);
        result.Suggestion.Evidence.Select(evidence => evidence.ResultId).Distinct().Should().Equal("r1", "r2", "r4", "r3");
        tools.Opened.Should().Be(new ToolContext("order-1", "X-Correlation-ID"));
    }

    [Fact]
    public async Task RunAsync_AnExceptionalOrder_SaysWhichExceptionIsOpenAndThatTheShipperWasNotified()
    {
        var order = """{"status":"Planned","shipmentNumber":"SH-0000042","shipmentStatus":"InTransit","rating":{"result":"Rated"},"billing":{"state":"NotYetBillable","blockingReasons":[]}}""";
        var exceptions = """{"exceptions":[{"exceptionNumber":"EX-0000007","type":"DeliveryFailed","status":"Open","shipperNotified":true},{"exceptionNumber":"EX-0000003","type":"Damaged","status":"Resolved","shipperNotified":false}]}""";

        var result = await OrderRunner(order, Timeline, exceptions, """{"status":"NotDelivered","proofsOfDelivery":[]}""", out _).RunAsync(Definition, OrderQuestion, CancellationToken.None);

        result.Suggestion.Cause.Should().Contain("2 Exceptions (delivery failed, damaged), 1 still open; the Shipper was notified of 1")
            .And.Contain("not been delivered").And.Contain("It is blocked");
        result.Suggestion.SuggestedAction.Should().Contain("EX-0000007 is still open");
    }

    [Fact]
    public async Task RunAsync_ADeliveredOrderThatIsNotYetBillable_NamesWhatBlocksIt()
    {
        var order = """{"status":"Planned","shipmentNumber":"SH-0000042","shipmentStatus":"Delivered","rating":{"result":"RatingFailed","failureCode":"NoMatchingLane","failureMessage":"No lane."},"billing":{"state":"NotYetBillable","blockingReasons":["NotRated","NoProofOfDelivery","OpenException","Other"]}}""";
        var proof = """{"status":"Missing","proofsOfDelivery":[{"isCurrent":false},{"isCurrent":false}]}""";
        var exceptions = """{"exceptions":[{"exceptionNumber":"EX-0000001","type":"LateDelivery","status":"Open"}]}""";

        var result = await OrderRunner(order, """{"trackingEvents":[]}""", exceptions, proof, out _).RunAsync(Definition, OrderQuestion, CancellationToken.None);

        result.Suggestion.Cause.Should().Contain("Rating failed with NoMatchingLane").And.Contain("No Carrier or Driver has reported").And.Contain("Proof of delivery is missing")
            .And.Contain("2 earlier Proofs of delivery were superseded").And.Contain("not yet billable");
        result.Suggestion.SuggestedAction.Should().Contain("not rated").And.Contain("no Proof of delivery").And.Contain("Exception on its Shipment is open")
            .And.Contain("Billing is blocked by Other.").And.Contain("EX-0000001");
        result.Suggestion.Evidence.Should().Contain(evidence => evidence.Claim.Contains("NoMatchingLane", StringComparison.Ordinal) && evidence.ResultId == "r1");
    }

    [Fact]
    public async Task RunAsync_AnOrderNotYetPlanned_ReadsOnlyTheOrder()
    {
        var order = """{"status":"Accepted","rating":{"result":"NotYetRated"},"billing":{"state":"NotYetBillable","blockingReasons":[]}}""";

        var result = await OrderRunner(order, Timeline, NoExceptions, CurrentProof, out _).RunAsync(Definition, OrderQuestion, CancellationToken.None);

        result.ToolCalls.Should().Equal(new ToolCallRecord("r1", "get_transport_order"));
        result.Suggestion.Cause.Should().Contain("no Shipment yet").And.Contain("not yet rated");
    }

    [Fact]
    public async Task RunAsync_WithReservationsAndOneSupersededProof_SaysSo()
    {
        var proof = """{"status":"Current","withReservations":true,"proofsOfDelivery":[{"isCurrent":false},{"isCurrent":true}]}""";

        var result = await OrderRunner(DeliveredOrder, Timeline, NoExceptions, proof, out _).RunAsync(Definition, OrderQuestion, CancellationToken.None);

        result.Suggestion.Cause.Should().Contain("taken with reservations").And.Contain("1 earlier Proof of delivery was superseded");
    }

    [Theory]
    [InlineData("NotDueYet", "not overdue yet")]
    [InlineData("Whatever", "unknown")]
    public async Task RunAsync_AProofStatusThatIsNeitherCurrentNorMissing_IsToldAsItIs(string status, string expected)
    {
        var result = await OrderRunner(DeliveredOrder, Timeline, NoExceptions, $$"""{"status":"{{status}}","proofsOfDelivery":[]}""", out _).RunAsync(Definition, OrderQuestion, CancellationToken.None);

        result.Suggestion.Cause.Should().Contain(expected);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"shipmentNumber":"SH-1"}""")]
    public async Task RunAsync_AnOrderResultWithNoStatus_IsToldWithLowConfidence(string order)
    {
        var result = await OrderRunner(order, Timeline, NoExceptions, CurrentProof, out _).RunAsync(Definition, OrderQuestion, CancellationToken.None);

        result.Suggestion.Confidence.Should().Be(Confidence.Low);
        result.Suggestion.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
    }
}
