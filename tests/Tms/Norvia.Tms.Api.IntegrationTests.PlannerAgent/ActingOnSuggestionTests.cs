using System.Net;
using System.Text.Json.Nodes;
using Norvia.Agents;

namespace Norvia.Tms.Api.IntegrationTests.PlannerAgent;

/// <summary>
/// Acting on a Suggestion to assign (P3), for a Transport Order each test submits itself. The Planner's confirmation is the TMS UI's call to the TMS API under an Idempotency-Key, and only
/// that call changes anything: the Suggestion itself executes nothing. The trace of the assignment names the Agent and its run (ADR 0040) when the Planner acted on a Suggestion, and
/// names none otherwise. A refusal leaves the order unassigned, so the Suggestion no longer holds (the TMS UI marks it out of date, covered by its component tests).
/// </summary>
public sealed class ActingOnSuggestionTests(PlannerAgentFixture fixture)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAssignmentMadeFromASuggestion_AssignsThroughTheTmsApi_AndNamesTheAgentAndItsRunOnTheTrace()
    {
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(5)), _cancellationToken);
        var agentId = PlannerAgentFixture.NewAgentId();
        var run = await PlannerAgentCalls.SuggestAsync(fixture, agentId, number, Guid.NewGuid().ToString(), _cancellationToken);
        var runId = run.RunId;

        var assigned = await fixture.AssignPlannedAsync(number, PlanningCalls.ToCarrier().ActingOn(agentId, runId), _cancellationToken);

        var trace = await fixture.TmsLogAsync(log => log["correlationId"] == assigned["correlationId"]!.GetValue<string>() && log["outcome"] == "Succeeded", _cancellationToken);
        trace["agentId"].Should().Be(agentId);
        trace["agentRunId"].Should().Be(runId);
        trace["transportOrderNumber"].Should().Be(number);
        (await fixture.GetTransportOrderAsync(number, _cancellationToken))["status"]!.GetValue<string>().Should().NotBe("Accepted", "the assignment planned it");
        (await fixture.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken)).Should().ContainSingle();
    }

    [Fact]
    public async Task TheSameAssignmentWithoutASuggestion_NamesNoAgentOnTheTrace()
    {
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(5)), _cancellationToken);

        var assigned = await fixture.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken);

        var trace = await fixture.TmsLogAsync(log => log["correlationId"] == assigned["correlationId"]!.GetValue<string>() && log["outcome"] == "Succeeded", _cancellationToken);
        trace["agentId"].Should().BeNull();
        trace["agentRunId"].Should().BeNull();
    }

    [Fact]
    public async Task AnAssignmentRepeatedUnderTheSameIdempotencyKey_IsAnsweredAsARepeat_AndPlansOnce()
    {
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(5)), _cancellationToken);
        var assignment = PlanningCalls.ToCarrier().ActingOn(PlannerAgentFixture.NewAgentId(), Guid.NewGuid().ToString("N"));
        var key = Guid.NewGuid().ToString();

        using var first = await fixture.AssignAsync(number, assignment, _cancellationToken, key);
        using var second = await fixture.AssignAsync(number, assignment, _cancellationToken, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(_cancellationToken));
        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync(_cancellationToken));
        var correlationId = JsonNode.Parse(await first.Content.ReadAsStringAsync(_cancellationToken))!["correlationId"]!.GetValue<string>();
        (await fixture.PublishedAsync(correlationId, "ShipmentPlanned", _cancellationToken)).Should().ContainSingle();
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        fixture.ShipmentsPlannedFor(number).Should().ContainSingle();
    }

    [Fact]
    public async Task AnIgnoredSuggestion_WritesNothing_TheOrderStaysAcceptedAndNothingIsPlanned()
    {
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(5)), _cancellationToken);
        var agentId = PlannerAgentFixture.NewAgentId();
        var correlationId = Guid.NewGuid().ToString();

        var run = await PlannerAgentCalls.SuggestAsync(fixture, agentId, number, correlationId, _cancellationToken);

        run.Suggestion.Should().NotBeNull();
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        (await fixture.GetTransportOrderAsync(number, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Accepted");
        fixture.ShipmentsPlannedFor(number).Should().BeEmpty();
        // The only trace is the Agent's reading of the order; no assignment, so none names the run as acted on.
        fixture.TmsLogs.Should().NotContain(log => log["agentRunId"] == run.RunId);
    }

    [Fact]
    public async Task ATmsRefusal_LeavesTheOrderUnassigned_SoTheSuggestionNoLongerHolds_AndIsRefusedOnTheTrail()
    {
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(5)), _cancellationToken);
        var agentId = PlannerAgentFixture.NewAgentId();
        var runId = (await PlannerAgentCalls.SuggestAsync(fixture, agentId, number, Guid.NewGuid().ToString(), _cancellationToken)).RunId;

        // The Carrier became inactive since the Agent suggested it.
        var refusal = await fixture.AssignRefusedAsync(
            number, PlanningCalls.ToCarrier(PlanningCalls.InactiveCarrierId).ActingOn(agentId, runId), HttpStatusCode.UnprocessableEntity, _cancellationToken);

        refusal["code"]!.GetValue<string>().Should().Be("InactiveCarrier");
        var trace = await fixture.TmsLogAsync(log => log["correlationId"] == refusal["correlationId"]!.GetValue<string>() && log["outcome"] == "Refused", _cancellationToken);
        trace["failureCode"].Should().Be("InactiveCarrier");
        trace["agentId"].Should().Be(agentId, "a refused assignment is still the Planner acting on the Suggestion");
        (await fixture.GetTransportOrderAsync(number, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Accepted");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        fixture.ShipmentsPlannedFor(number).Should().BeEmpty();
    }
}
