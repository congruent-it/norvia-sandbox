using System.Net;
using System.Text.Json.Nodes;
using Norvia.Agents;
using Norvia.Agents.Runner;

namespace Norvia.Tms.Api.IntegrationTests.CustomerServiceAgent;

/// <summary>
/// The release-without-Proof-of-delivery recommendation (P9), for Transport Orders each test carries to the state itself. <c>get_release_eligibility</c> gives what the release command
/// itself decides, in every state that matters (not Delivered, Delivered, Billable already, released already, an open Exception, not rated); an order that is not eligible never gets a
/// Release suggestion; and the release's trace names the Agent and its run only when the employee acted on a Suggestion. The Agent runs in the test as the TMS UI runs it in process.
/// </summary>
public sealed class RecommendReleaseTests(CustomerServiceAgentFixture fixture)
{
    private const string EligibilityTool = "get_release_eligibility";

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADeliveredRatedOrderWithoutAProofOfDelivery_IsEligible_AndTheReleaseCommandAcceptsIt()
    {
        var order = await DeliveredOrderAsync();

        var eligibility = await EligibilityAsync(order.Number);
        var released = await fixture.ReleasedWithoutProofAsync(order.Number, _cancellationToken);

        eligibility["eligible"]!.GetValue<bool>().Should().BeTrue();
        eligibility["blockingReasons"]!.AsArray().Should().BeEmpty();
        eligibility["shipmentNumber"]!.GetValue<string>().Should().Be(order.Booked.ShipmentNumber);
        eligibility["deliveredAt"]!.GetValue<DateTimeOffset>().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        released["billingState"]!.GetValue<string>().Should().Be("Billable");
    }

    [Fact]
    public async Task AnOrderWhoseShipmentIsNotDelivered_IsNotEligible_AndTheReleaseCommandRefusesItForTheSameReason()
    {
        var number = await RatedOrderAsync();
        await fixture.BookedAsync(number, _cancellationToken);

        var eligibility = await EligibilityAsync(number);
        var refusal = await fixture.ReleaseRefusedAsync(number, BillingCalls.Release(), HttpStatusCode.Conflict, _cancellationToken);

        ReasonsOf(eligibility).Should().Equal("ShipmentNotDelivered");
        refusal["code"]!.GetValue<string>().Should().Be("ShipmentNotDelivered");
        await AssertNoReleaseSuggestionAsync(number);
    }

    [Fact]
    public async Task AnOrderThatIsBillableAlready_IsNotEligible_AndTheReleaseCommandRefusesItForTheSameReason()
    {
        var order = await DeliveredOrderAsync();
        await fixture.ProofRecordedAsync(order.Booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await fixture.WaitForBillableAsync(order.Number, _cancellationToken);

        var eligibility = await EligibilityAsync(order.Number);
        var refusal = await fixture.ReleaseRefusedAsync(order.Number, BillingCalls.Release(), HttpStatusCode.Conflict, _cancellationToken);

        ReasonsOf(eligibility).Should().Contain("AlreadyBillable").And.Contain("ProofOfDeliveryReceived");
        refusal["code"]!.GetValue<string>().Should().Be("AlreadyBillable");
        await AssertNoReleaseSuggestionAsync(order.Number);
    }

    [Fact]
    public async Task AnOrderReleasedAlready_IsNotEligible_AndTheReleaseCommandRefusesItForTheSameReason()
    {
        var number = await SubmitNoMatchingLaneAsync();
        var booked = await fixture.BookedAsync(number, _cancellationToken);
        await fixture.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await fixture.ReleasedWithoutProofAsync(number, _cancellationToken);

        var eligibility = await EligibilityAsync(number);
        var refusal = await fixture.ReleaseRefusedAsync(number, BillingCalls.Release("again", "Piet"), HttpStatusCode.Conflict, _cancellationToken);

        ReasonsOf(eligibility).Should().Contain("AlreadyReleased");
        refusal["code"]!.GetValue<string>().Should().Be("AlreadyReleased");
        await AssertNoReleaseSuggestionAsync(number);
    }

    [Fact]
    public async Task AnOrderThatIsNotRated_IsNotEligible_BecauseReleasingWouldNotMakeItBillable_AndGetsNoReleaseSuggestion()
    {
        var number = await SubmitNoMatchingLaneAsync();
        var booked = await fixture.BookedAsync(number, _cancellationToken);
        await fixture.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);

        var eligibility = await EligibilityAsync(number);

        eligibility["eligible"]!.GetValue<bool>().Should().BeFalse();
        ReasonsOf(eligibility).Should().Equal("NotRated");
        await AssertNoReleaseSuggestionAsync(number);
    }

    [Fact]
    public async Task AnOrderWithAnOpenException_IsNotEligible_BecauseReleasingWouldNotMakeItBillable_AndGetsNoReleaseSuggestion()
    {
        var order = await DeliveredOrderAsync();
        await fixture.ProofWithReservationsRecordedAsync(order.Booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        (await fixture.OpenExceptionsOfAsync(order.Booked.ShipmentNumber, _cancellationToken)).Should().ContainSingle();

        var eligibility = await EligibilityAsync(order.Number);

        eligibility["eligible"]!.GetValue<bool>().Should().BeFalse();
        ReasonsOf(eligibility).Should().Contain("OpenException");
        await AssertNoReleaseSuggestionAsync(order.Number);
    }

    [Fact]
    public async Task AnEligibleOrder_GetsAWaitOrReleaseSuggestion_WithoutADraftedRemark_AndNothingIsReleased()
    {
        var order = await DeliveredOrderAsync();

        var run = await CustomerServiceAgentCalls.AskAsync(
            fixture, CustomerServiceAgentFixture.NewAgentId(), CustomerServiceQuestions.RecommendRelease(order.Number), TmsCalls.NewCorrelationId(), _cancellationToken);

        run.ToolCalls.Select(call => call.ToolName).Should().StartWith(EligibilityTool);
        run.Suggestion.Action.Should().BeOneOf(SuggestedActionKind.Release, SuggestedActionKind.Wait);
        run.Suggestion.SuggestedAction.Should().NotContainEquivalentOf("remark:").And.NotContainEquivalentOf("CMR received");
        (await fixture.GetTransportOrderAsync(order.Number, _cancellationToken))["proofOfDeliveryRelease"].Should().BeNull("a Suggestion executes nothing");
    }

    [Fact]
    public async Task AReleaseMadeFromASuggestion_NamesTheAgentAndItsRunOnTheTrace()
    {
        var order = await DeliveredOrderAsync();
        var agentName = CustomerServiceAgentFixture.NewAgentId();
        var runId = Guid.NewGuid().ToString("N");

        using var response = await fixture.ReleaseWithoutProofOfDeliveryAsync(order.Number, BillingCalls.Release(agentName: agentName, agentRunId: runId), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(_cancellationToken));
        var correlationId = JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!["correlationId"]!.GetValue<string>();
        var trace = await fixture.TmsLogAsync(log => log["correlationId"] == correlationId && log["outcome"] == "Succeeded", _cancellationToken);
        trace["agentId"].Should().Be(agentName);
        trace["agentRunId"].Should().Be(runId);
    }

    [Fact]
    public async Task AReleaseTheEmployeeMakesOnTheirOwn_NamesNoAgentOnTheTrace()
    {
        var order = await DeliveredOrderAsync();

        var released = await fixture.ReleasedWithoutProofAsync(order.Number, _cancellationToken);

        var correlationId = released["correlationId"]!.GetValue<string>();
        var trace = await fixture.TmsLogAsync(log => log["correlationId"] == correlationId && log["outcome"] == "Succeeded", _cancellationToken);
        trace["agentId"].Should().BeNull();
        trace["agentRunId"].Should().BeNull();
    }

    private async Task<JsonNode> EligibilityAsync(string number)
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken());
        var (isError, text) = await client.CallToolAsync(EligibilityTool, new JsonObject { ["transportOrderNumber"] = number }, Guid.NewGuid().ToString(), _cancellationToken);
        isError.Should().BeFalse(text);
        return JsonNode.Parse(text)!;
    }

    private static string[] ReasonsOf(JsonNode eligibility) => [.. eligibility["blockingReasons"]!.AsArray().Select(reason => reason!.GetValue<string>())];

    /// <summary>The Agent, asked about an order the TMS says is not eligible, never suggests Release and says why it cannot.</summary>
    private async Task AssertNoReleaseSuggestionAsync(string number)
    {
        var run = await CustomerServiceAgentCalls.AskAsync(
            fixture, CustomerServiceAgentFixture.NewAgentId(), CustomerServiceQuestions.RecommendRelease(number), TmsCalls.NewCorrelationId(), _cancellationToken);

        run.Suggestion.Action.Should().NotBe(SuggestedActionKind.Release);
        run.Suggestion.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
        run.Suggestion.Evidence.Should().NotBeEmpty().And.OnlyContain(evidence => evidence.ToolName == EligibilityTool);
    }

    private sealed record Delivered(string Number, BillingCalls.BookedOrder Booked);

    private async Task<string> RatedOrderAsync()
    {
        var number = await fixture.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);
        await fixture.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        return number;
    }

    /// <summary>A booking to Düsseldorf, where the test seed's Rate Card has no Lane, so Rating fails.</summary>
    private async Task<string> SubmitNoMatchingLaneAsync()
    {
        var number = await fixture.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), TmsCalls.NewCorrelationId(), _cancellationToken);
        await fixture.WaitForRatingResultAsync(number, "RatingFailed", _cancellationToken);
        return number;
    }

    /// <summary>A Transport Order that is rated, Booked with the Carrier and reported Delivered; what else happens to it is the test's own.</summary>
    private async Task<Delivered> DeliveredOrderAsync()
    {
        var number = await RatedOrderAsync();
        var booked = await fixture.BookedAsync(number, _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();
        await fixture.DeliveredAsync(booked, correlationId, _cancellationToken);
        await fixture.PublishedAsync(correlationId, "ShipmentDelivered", _cancellationToken);
        return new Delivered(number, booked);
    }
}
