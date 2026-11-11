using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.PlannerAgent;

/// <summary>
/// Acting on a Suggestion to assign (P3), for a Transport Order each test submits itself. The Planner's confirmation is the TMS UI's call to the TMS API under an Idempotency-Key, and only
/// that call changes anything: the Suggestion itself executes nothing. The trace of the assignment names the Agent (its name is the trace's <c>agentId</c>) and its run (ADR 0040) when the
/// Planner acted on a Suggestion, and names none otherwise. A refusal leaves the order unassigned, so the Suggestion no longer holds; that the TMS UI then marks it out of date is
/// covered by its component tests.
/// </summary>
public sealed class ActingOnSuggestionTests(PlannerAgentFixture fixture)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAssignmentMadeFromASuggestion_AssignsThroughTheTmsApiUnderAnIdempotencyKey_AndNamesTheAgentAndItsRunOnTheTrace()
    {
        var number = await AcceptedOrderAsync();
        var agentName = PlannerAgentFixture.NewAgentId();
        var run = await fixture.SuggestAsync(agentName, number, Guid.NewGuid().ToString(), _cancellationToken);
        var key = Guid.NewGuid().ToString();

        using var response = await fixture.AssignAsync(number, PlanningCalls.ToCarrier().ActingOn(agentName, run.RunId), _cancellationToken, key);

        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        var correlationId = JsonNode.Parse(body)!["correlationId"]!.GetValue<string>();
        var trace = await SucceededTraceAsync(correlationId);
        trace["agentId"].Should().Be(agentName);
        trace["agentRunId"].Should().Be(run.RunId);
        trace["transportOrderNumber"].Should().Be(number);
        (await fixture.GetTransportOrderAsync(number, _cancellationToken))["status"]!.GetValue<string>().Should().NotBe("Accepted", "the assignment planned it");
        await fixture.PublishedAsync(correlationId, "ShipmentPlanned", _cancellationToken);
        using var repeat = await fixture.AssignAsync(number, PlanningCalls.ToCarrier().ActingOn(agentName, run.RunId), _cancellationToken, key);
        repeat.StatusCode.Should().Be(HttpStatusCode.OK, "the same Idempotency-Key is answered as a repeat");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        fixture.ShipmentsPlannedFor(number).Should().ContainSingle("the repeat plans nothing more");
    }

    [Fact]
    public async Task TheSameAssignmentWithoutASuggestion_NamesNoAgentOnTheTrace()
    {
        var number = await AcceptedOrderAsync();

        var assigned = await fixture.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken);

        var trace = await SucceededTraceAsync(assigned["correlationId"]!.GetValue<string>());
        trace["agentId"].Should().BeNull();
        trace["agentRunId"].Should().BeNull();
    }

    [Fact]
    public async Task AnIgnoredSuggestion_WritesNothing()
    {
        var number = await AcceptedOrderAsync();
        var agentName = PlannerAgentFixture.NewAgentId();

        await fixture.SuggestAsync(agentName, number, Guid.NewGuid().ToString(), _cancellationToken);

        // The Agent's reading is the only thing on its trail: the order is still unassigned, nothing is planned, and no trace outside the read-only agent view names the Agent.
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        (await fixture.GetTransportOrderAsync(number, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Accepted");
        fixture.ShipmentsPlannedFor(number).Should().BeEmpty();
        fixture.TmsLogs.Where(log => log["agentId"] == agentName).Should().OnlyContain(log => log["flow"] == PlannerAgentCalls.AgentViewFlow);
    }

    [Fact]
    public async Task ARefusedAssignment_LeavesTheOrderUnassigned_AndIsRefusedOnTheTrailNamingTheAgent()
    {
        var number = await AcceptedOrderAsync();
        var agentName = PlannerAgentFixture.NewAgentId();
        var run = await fixture.SuggestAsync(agentName, number, Guid.NewGuid().ToString(), _cancellationToken);

        // The Carrier became inactive since the Agent suggested it.
        var refusal = await fixture.AssignRefusedAsync(
            number, PlanningCalls.ToCarrier(PlanningCalls.InactiveCarrierId).ActingOn(agentName, run.RunId), HttpStatusCode.UnprocessableEntity, _cancellationToken);

        refusal["code"]!.GetValue<string>().Should().Be("InactiveCarrier");
        var correlationId = refusal["correlationId"]!.GetValue<string>();
        var trace = await fixture.TmsLogAsync(log => log["correlationId"] == correlationId && log["outcome"] == "Refused", _cancellationToken);
        trace["failureCode"].Should().Be("InactiveCarrier");
        trace["agentId"].Should().Be(agentName, "a refused assignment is still the Planner acting on the Suggestion");
        trace["agentRunId"].Should().Be(run.RunId);
        (await fixture.GetTransportOrderAsync(number, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Accepted");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        fixture.ShipmentsPlannedFor(number).Should().BeEmpty();
    }

    private Task<string> AcceptedOrderAsync() =>
        PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(5)), _cancellationToken);

    private Task<TmsLog> SucceededTraceAsync(string correlationId) =>
        fixture.TmsLogAsync(log => log["correlationId"] == correlationId && log["outcome"] == "Succeeded", _cancellationToken);
}
