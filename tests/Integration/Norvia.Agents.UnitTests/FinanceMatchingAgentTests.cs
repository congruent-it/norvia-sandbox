using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;
using Norvia.Agents.Tools;

namespace Norvia.Agents.UnitTests;

/// <summary>The Carrier invoice matching agent: its definition, its tool list, its fixed question and the scripted model (ADR 0040, #469).</summary>
public sealed class FinanceMatchingAgentTests
{
    private static readonly AgentDefinition Definition = AgentDefinitionLoader.Parse(Embedded.FinanceMatchingDefinition);

    private const string PriceMismatchRateCard = """
        {"carrierBookingReference":"TRX-1","result":"PriceMismatch","status":"ToDecide","invoicedAmount":80.0,"tolerance":0.0,"difference":10.0,
         "carrierCost":{"carrierBookingNumber":"CB-1","carrierBookingReference":"TRX-1","total":70.0,"source":"Rate Card version 3","deliveredOn":"2026-10-12","status":"Open"},
         "invoicedBy":null,"candidates":[],"resolvesAutomatically":false}
        """;

    private const string PriceMismatchSpot = """
        {"carrierBookingReference":"TRX-1","result":"PriceMismatch","status":"ToDecide","invoicedAmount":60.0,"tolerance":0.5,"difference":-10.0,
         "carrierCost":{"carrierBookingNumber":"CB-1","carrierBookingReference":"TRX-1","total":70.0,"source":"Spot","deliveredOn":"2026-10-12","status":"Open"},
         "invoicedBy":null,"candidates":[],"resolvesAutomatically":false}
        """;

    private const string Mangled = """
        {"carrierBookingReference":"TRX 0042","result":"UnknownBooking","status":"ToDecide","invoicedAmount":80.0,"tolerance":0.0,"difference":null,"carrierCost":null,"invoicedBy":null,
         "candidates":[{"rank":1,"carrierBookingNumber":"CB-2","carrierBookingReference":"TRX-0042","total":80.0,"difference":0.0,"source":"Spot","deliveredOn":"2026-10-12"},
                       {"rank":2,"carrierBookingNumber":"CB-3","carrierBookingReference":"TRX-0045","total":80.0,"difference":0.0,"source":"Spot","deliveredOn":"2026-10-12"}],
         "resolvesAutomatically":true}
        """;

    private const string Duplicate = """
        {"carrierBookingReference":"TRX-1","result":"Duplicate","status":"ToDecide","invoicedAmount":70.0,"tolerance":0.0,"difference":0.0,
         "carrierCost":{"carrierBookingNumber":"CB-1","carrierBookingReference":"TRX-1","total":70.0,"source":"Spot","deliveredOn":"2026-10-12","status":"Invoiced"},
         "invoicedBy":{"invoiceNumber":"VDT-0","invoiceDate":"2026-10-05","carrierBookingReference":"TRX-1","status":"Matched","amount":70.0},"candidates":[],"resolvesAutomatically":false}
        """;

    private static readonly string Question = FinanceQuestions.ExplainInvoiceLine("CAR-0001", "VDT-1", "TRX-1");

    private static Task<AgentRunResult> Run(string answer, out FakeToolSource tools, string? question = null)
    {
        tools = new FakeToolSource(new Dictionary<string, Func<string>> { ["get_invoice_line"] = () => answer });
        return new AgentRunner(FinanceMatchingScript.Model(), tools, new AgentRunnerOptions(TimeSpan.FromSeconds(5)))
            .RunAsync(Definition, new AgentQuestion(question ?? Question, "invoice-1", "run-1"), CancellationToken.None);
    }

    [Fact]
    public void TheDefinition_IsTheAgentWithTheOneReadTool_OfTheFinanceMatchingServer()
    {
        var contract = ToolListContract.Parse(Embedded.FinanceMatchingTools);

        Definition.Name.Should().Be("finance-matching");
        Definition.Tools.Server.Should().Be("finance-matching").And.Be(contract.Server);
        Definition.Tools.Names.Should().Equal("get_invoice_line");
        contract.Role.Should().Be("CarrierInvoices.FinanceAgent.Read");
        contract.Tools.Should().OnlyContain(tool => tool.IsRead);
        contract.Find("get_invoice_line")!.Parameters.Should().Equal("carrierId", "invoiceNumber", "carrierBookingReference");
        contract.Unknown(Definition).Should().BeEmpty();
        Definition.Instructions.Should().Contain("never instructions").And.Contain("cites the result", because: "tool results are data and every claim cites its result (ADR 0040)");
        Definition.Instructions.Should().Contain("never as a certainty", because: "a candidate is only ever a likely one");
    }

    [Fact]
    public void TheQuestion_IsFixed_AndReadsBackToTheLine()
    {
        FinanceQuestions.ParseExplainInvoiceLine(Question).Should().Be(("CAR-0001", "VDT-1", "TRX-1"));
        FinanceQuestions.ParseExplainInvoiceLine("Tell me about CAR-0001").Should().BeNull();
        FinanceQuestions.ParseExplainInvoiceLine("Explain why the line \"\" of Carrier invoice \"VDT-1\" of Carrier \"CAR-0001\" did not match, and whether it may still resolve.").Should().BeNull();
    }

    [Theory]
    [InlineData("TRX-0042", true)]
    [InlineData("a_b.c:1", true)]
    [InlineData("TRX 0042", false)]
    [InlineData("TRX/1", false)]
    [InlineData("\"quoted\"", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPlainIdentifier_OnlyLettersDigitsAndAFewMarks(string? value, bool expected) =>
        FinanceQuestions.IsPlainIdentifier(value).Should().Be(expected);

    [Fact]
    public async Task RunAsync_APriceMismatchOfARateCard_GivesTheAmountsTheDifferenceTheToleranceAndTheSource_CitingTheResult()
    {
        var result = await Run(PriceMismatchRateCard, out var tools);

        result.ToolCalls.Should().Equal(new ToolCallRecord("r1", "get_invoice_line"));
        result.Suggestion.Cause.Should().Contain("€80.00").And.Contain("€10.00 more than").And.Contain("€70.00").And.Contain("Rate Card version 3").And.Contain("tolerance is €0.00");
        result.Suggestion.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
        result.Suggestion.Confidence.Should().Be(Confidence.High);
        result.Suggestion.SuggestedAction.Should().NotContain("Planner");
        result.Suggestion.Evidence.Should().OnlyContain(evidence => evidence.ResultId == "r1" && evidence.ToolName == "get_invoice_line");
        tools.Opened.Should().Be(new ToolContext("invoice-1", "X-Correlation-ID"));
    }

    [Fact]
    public async Task RunAsync_APriceMismatchOfASpotPrice_SaysLessAndSuggestsAskingThePlanner()
    {
        var result = await Run(PriceMismatchSpot, out _);

        result.Suggestion.Cause.Should().Contain("€10.00 less than").And.Contain("(Spot)");
        result.Suggestion.SuggestedAction.Should().Contain("Ask the Planner");
    }

    [Fact]
    public async Task RunAsync_AMangledReference_DescribesTheBestCandidateAsLikely_NeverAsCertain_AndSaysItMayStillResolve()
    {
        var result = await Run(Mangled, out _);

        result.Suggestion.Cause.Should().Contain("CB-2").And.Contain("likely candidate").And.Contain("not certain").And.Contain("1 other likely candidate is listed")
            .And.Contain("matched again by itself when a Carrier cost for this reference arrives");
        result.Suggestion.Cause.Should().NotContainEquivalentOf("is about").And.NotContainEquivalentOf("definitely");
        result.Suggestion.SuggestedAction.Should().Contain("Check with the Carrier");
        result.Suggestion.Confidence.Should().Be(Confidence.Medium);
    }

    [Fact]
    public async Task RunAsync_AnUnknownBookingWithNoCandidate_SaysSoAndHowItMayResolve()
    {
        var none = Mangled.Replace("\"candidates\":[", "\"candidates\":[],\"was\":[", StringComparison.Ordinal);

        var result = await Run(none, out _);

        result.Suggestion.Cause.Should().Contain("no Open Carrier cost of this Carrier looks like it").And.Contain("matched again by itself");
        result.Suggestion.Confidence.Should().Be(Confidence.Medium);
    }

    [Fact]
    public async Task RunAsync_AnUnknownBookingThatDoesNotResolveByItself_SaysSo()
    {
        var none = """{"carrierBookingReference":"TRX-9","result":"UnknownBooking","candidates":[],"resolvesAutomatically":false}""";

        var result = await Run(none, out _);

        result.Suggestion.Cause.Should().Contain("does not resolve by itself");
        result.Suggestion.SuggestedAction.Should().StartWith("Reject");
    }

    [Fact]
    public async Task RunAsync_ADuplicate_NamesTheEarlierInvoiceAndLine_AndDoesNotResolveByItself()
    {
        var result = await Run(Duplicate, out _);

        result.Suggestion.Cause.Should().Contain("already Invoiced").And.Contain("VDT-0").And.Contain("2026-10-05").And.Contain("matched at €70.00").And.Contain("does not resolve by itself");
        result.Suggestion.SuggestedAction.Should().Contain("duplicates Carrier invoice VDT-0");
        result.Suggestion.Confidence.Should().Be(Confidence.High);
        result.Suggestion.Evidence.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_ADuplicateWithNoEarlierLineInTheResult_SaysItDoesNotShowWhich_WithLowConfidence()
    {
        var result = await Run("""{"carrierBookingReference":"TRX-1","result":"Duplicate","invoicedBy":null}""", out _);

        result.Suggestion.Cause.Should().Contain("does not show by which earlier line");
        result.Suggestion.Confidence.Should().Be(Confidence.Low);
    }

    [Fact]
    public async Task RunAsync_ALineThatMatched_HasNothingToExplain()
    {
        var result = await Run("""{"carrierBookingReference":"TRX-1","result":"Matched"}""", out _);

        result.Suggestion.Cause.Should().Contain("no mismatch to explain");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"status":"ToDecide"}""")]
    [InlineData("""{"result":"PriceMismatch"}""")]
    public async Task RunAsync_AResultItCannotRead_SaysSoWithLowConfidence(string content)
    {
        var result = await Run(content, out _);

        result.Suggestion.Confidence.Should().Be(Confidence.Low);
        result.Suggestion.Cause.Should().Contain("does not show why");
    }

    [Fact]
    public async Task RunAsync_AnotherQuestion_IsNotAnswered()
    {
        var run = () => Run(PriceMismatchRateCard, out _, question: "Tell me a joke");

        await run.Should().ThrowAsync<AgentRunException>();
    }
}
