using Norvia.Agents;
using Norvia.Agents.Runner;

namespace Norvia.Tms.Api.IntegrationTests.CustomerServiceAgent;

/// <summary>
/// The Agent's answers to the two questions on the rating exceptions and not-yet-billable lists, for Transport Orders each test carries to the state itself: one whose Rating failed
/// with <c>NoMatchingLane</c>, one held back by <c>NoProofOfDelivery</c> and one held back by an open Exception. The Agent runs in the test as the TMS UI runs it in process, with the
/// scripted model, reading the order through the stand-in and the TMS API's agent view. It offers only what Customer service can do: it never tells them to edit a Rate Card, resolve an
/// Exception or change a Shipment.
/// </summary>
public sealed class ExplainOrderHoldTests(CustomerServiceAgentFixture fixture)
{
    private const string TransportOrderTool = "get_transport_order";

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnOrderWhoseRatingFailedWithNoMatchingLane_IsReadWithItsFailure_AndExplainedWithTheRateCardLeftToItsOwner()
    {
        var number = await SubmitNoMatchingLaneAsync();
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (isError, text) = await client.CallToolAsync(TransportOrderTool, new System.Text.Json.Nodes.JsonObject { ["transportOrderNumber"] = number }, Guid.NewGuid().ToString(), _cancellationToken);
        var run = await CustomerServiceAgentCalls.AskAsync(
            fixture, CustomerServiceAgentFixture.NewAgentId(), CustomerServiceQuestions.ExplainRatingFailure(number), TmsCalls.NewCorrelationId(), _cancellationToken);

        isError.Should().BeFalse(text);
        var order = System.Text.Json.Nodes.JsonNode.Parse(text)!;
        order["rating"]!["result"]!.GetValue<string>().Should().Be("RatingFailed");
        order["rating"]!["failureCode"]!.GetValue<string>().Should().Be("NoMatchingLane");
        run.ToolCalls.Select(call => call.ToolName).Should().Equal(TransportOrderTool);
        run.Suggestion.Cause.Should().Contain("no Lane for this origin and destination");
        run.Suggestion.SuggestedAction.Should().StartWith("Customer service does not edit the Rate Card")
            .And.Contain("ask whoever owns the Rate Card to fix it").And.EndWith("Re-rate action on the rating exceptions list.");
        run.Suggestion.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
        run.Suggestion.Confidence.Should().Be(Confidence.High);
        run.Suggestion.Evidence.Should().NotBeEmpty().And.OnlyContain(evidence => evidence.ToolName == TransportOrderTool);
    }

    [Fact]
    public async Task ADeliveredOrderWithoutAProofOfDelivery_IsExplainedAsHeldByIt_WithTheActionsCustomerServiceCanTake()
    {
        var number = await DeliveredOrderAsync();
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (isError, text) = await client.CallToolAsync(TransportOrderTool, new System.Text.Json.Nodes.JsonObject { ["transportOrderNumber"] = number.Number }, Guid.NewGuid().ToString(), _cancellationToken);
        var run = await CustomerServiceAgentCalls.AskAsync(
            fixture, CustomerServiceAgentFixture.NewAgentId(), CustomerServiceQuestions.ExplainNotYetBillable(number.Number), TmsCalls.NewCorrelationId(), _cancellationToken);

        isError.Should().BeFalse(text);
        System.Text.Json.Nodes.JsonNode.Parse(text)!["billing"]!["blockingReasons"]!.AsArray().Select(reason => reason!.GetValue<string>()).Should().Equal("NoProofOfDelivery");
        run.Suggestion.Cause.Should().Contain("has no Proof of delivery");
        run.Suggestion.SuggestedAction.Should().Contain("ask the Carrier for the Proof of delivery, or release the order without one")
            .And.NotContain("Planner").And.NotContain("Rate Card");
        run.Suggestion.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
    }

    [Fact]
    public async Task AnOrderHeldByAnOpenException_IsExplainedAsNotCustomerServicesToAct_OnlyThePlannerResolvesIt()
    {
        var number = await DeliveredOrderAsync();
        await fixture.ProofWithReservationsRecordedAsync(number.Booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        (await fixture.OpenExceptionsOfAsync(number.Booked.ShipmentNumber, _cancellationToken)).Should().ContainSingle();
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (isError, text) = await client.CallToolAsync(TransportOrderTool, new System.Text.Json.Nodes.JsonObject { ["transportOrderNumber"] = number.Number }, Guid.NewGuid().ToString(), _cancellationToken);
        var run = await CustomerServiceAgentCalls.AskAsync(
            fixture, CustomerServiceAgentFixture.NewAgentId(), CustomerServiceQuestions.ExplainNotYetBillable(number.Number), TmsCalls.NewCorrelationId(), _cancellationToken);

        isError.Should().BeFalse(text);
        System.Text.Json.Nodes.JsonNode.Parse(text)!["billing"]!["blockingReasons"]!.AsArray().Select(reason => reason!.GetValue<string>()).Should().Equal("OpenException");
        run.Suggestion.Cause.Should().Contain("still open");
        run.Suggestion.SuggestedAction.Should().StartWith("Customer service can do nothing about this yet.")
            .And.Contain("Planner has to resolve it")
            .And.NotContain("re-rate").And.NotContain("release the order");
        run.Suggestion.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
    }

    [Fact]
    public async Task AnOrderThatIsRatedAndBillable_IsExplainedAsHeldByNothing()
    {
        var delivered = await DeliveredOrderAsync();
        await fixture.ProofRecordedAsync(delivered.Booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await fixture.WaitForBillableAsync(delivered.Number, _cancellationToken);

        var run = await CustomerServiceAgentCalls.AskAsync(
            fixture, CustomerServiceAgentFixture.NewAgentId(), CustomerServiceQuestions.ExplainNotYetBillable(delivered.Number), TmsCalls.NewCorrelationId(), _cancellationToken);

        run.Suggestion.Cause.Should().Contain("Nothing holds");
        run.Suggestion.SuggestedAction.Should().StartWith("Nothing needs doing");
    }

    private sealed record Delivered(string Number, BillingCalls.BookedOrder Booked);

    /// <summary>A booking to Düsseldorf, where the test seed's Rate Card has no Lane, so Rating fails with NoMatchingLane.</summary>
    private async Task<string> SubmitNoMatchingLaneAsync()
    {
        var number = await fixture.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), TmsCalls.NewCorrelationId(), _cancellationToken);
        await fixture.WaitForRatingResultAsync(number, "RatingFailed", _cancellationToken);
        return number;
    }

    /// <summary>A Transport Order that is rated, Booked with the Carrier and reported Delivered; what else happens to it is the test's own.</summary>
    private async Task<Delivered> DeliveredOrderAsync()
    {
        var number = await fixture.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);
        await fixture.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        var booked = await fixture.BookedAsync(number, _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();
        await fixture.DeliveredAsync(booked, correlationId, _cancellationToken);
        await fixture.PublishedAsync(correlationId, "ShipmentDelivered", _cancellationToken);
        return new Delivered(number, booked);
    }
}
