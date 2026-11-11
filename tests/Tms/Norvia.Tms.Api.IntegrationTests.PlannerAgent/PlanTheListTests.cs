using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.TestSupport.Capacity;

namespace Norvia.Tms.Api.IntegrationTests.PlannerAgent;

/// <summary>
/// Planning the waiting list (#445), for Transport Orders each test submits itself, on a collection day of its own so no other test changes the day's load. The what-if tally is the TMS's:
/// <c>get_assignment_options_assuming</c> counts the orders already suggested for Own fleet in the load check, and nothing is assigned. A run is the real <see cref="PlanTheListHandler"/> over the real
/// Planner agent and the stand-in; its cap on orders is its cap on tool calls, as the Agent reads once per order.
/// </summary>
public sealed class PlanTheListTests(PlannerAgentFixture fixture)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    // A tally of this many orders is past the stand-in's 200-character limit on a path segment (and past the TMS's own cap of 50).
    private const int OrdersPastTheStandInsLimit = 51;
    private const string AnOrderThatDoesNotExist = "TO-9999999";

    [Fact]
    public async Task OptionsAssuming_AnAssumedOrderOnTheSameDay_MakesTwoOrdersThatEachFitOverCapacityTogether()
    {
        var (first, second) = await TwoOrdersThatTipTheDayTogetherAsync(PlannerAgentCalls.Weekday(6));
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var alone = await client.OptionsOfAsync(second, _cancellationToken);
        var assuming = await client.OptionsAssumingAsync(second, first, _cancellationToken);

        alone["ownFleet"]!["fit"]!.GetValue<string>().Should().Be("Fits");
        alone["ownFleet"]!["warning"].Should().BeNull();
        var own = assuming["ownFleet"]!;
        own["fit"]!.GetValue<string>().Should().Be("OverCapacity");
        own["warning"].Should().NotBeNull("the warning the assignment would show is named");
        own["load"]!["over"]!.GetValue<bool>().Should().BeTrue();
        DemandKgOf(assuming).Should().BeGreaterThan(DemandKgOf(alone));
    }

    [Fact]
    public async Task OptionsAssuming_AssignsNothing()
    {
        var (first, second) = await TwoOrdersThatTipTheDayTogetherAsync(PlannerAgentCalls.Weekday(6));
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        await client.OptionsAssumingAsync(second, first, _cancellationToken);

        (await StatusOfAsync(first)).Should().Be("Accepted", "a what-if assigns nothing");
        (await StatusOfAsync(second)).Should().Be("Accepted");
    }

    [Fact]
    public async Task OptionsAssuming_CountsOnlyAWaitingOrderOfTheSameDay_NotAnUnknownOne_TheOrderItself_AnotherDaysOrAPlannedOne()
    {
        var day = PlannerAgentCalls.Weekday(7);
        var order = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(day), _cancellationToken);
        var waiting = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(day), _cancellationToken);
        var otherDay = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(8)), _cancellationToken);
        var planned = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(day), _cancellationToken);
        await PlannerAgentCalls.PlannedAsync(fixture, planned, PlanningCalls.ToOwnFleet(), _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var plain = await client.OptionsOfAsync(order, _cancellationToken);
        var ignored = await client.OptionsAssumingAsync(order, $"{AnOrderThatDoesNotExist},{order},{otherDay},{planned}", _cancellationToken);
        var counted = await client.OptionsAssumingAsync(order, waiting, _cancellationToken);

        // The planned one is in the day's demand already, so counting it again would double it; the waiting one is the positive control.
        DemandKgOf(ignored).Should().Be(DemandKgOf(plain));
        DemandKgOf(counted).Should().BeGreaterThan(DemandKgOf(plain), "an order waiting for planning on the same day is counted");
    }

    [Fact]
    public async Task OptionsAssuming_ATallyTooLongToBeAnIdentifierList_IsRefusedByTheServer()
    {
        var order = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(6)), _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());
        var tooMany = string.Join(',', Enumerable.Range(1, OrdersPastTheStandInsLimit).Select(n => $"TO-{n:0000000}"));

        using var response = await client.CallToolRawAsync(
            PlannerAgentCalls.AssignmentOptionsAssumingTool,
            new JsonObject { ["transportOrderNumber"] = order, ["alreadyAssumed"] = tooMany },
            _cancellationToken);

        var message = (await response.Content.ReadFromJsonAsync<JsonObject>(_cancellationToken))!;
        message["result"].Should().BeNull();
        message["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }

    [Fact]
    public async Task PlanTheList_AnOrderThatFitsOwnFleetAlone_IsSuggestedForOwnFleetWithNoWarning()
    {
        var (first, second) = await TwoOrdersThatTipTheDayTogetherAsync(PlannerAgentCalls.Weekday(8));

        var run = await PlanTheListRun.ExecuteAsync(fixture, [first, second], maxOrders: 10, _cancellationToken);

        run.Plan.Stopped.Should().BeNull();
        run.Plan.Suggestions.Select(listed => listed.TransportOrderNumber).Should().Equal(first, second);
        var one = run.Plan.Suggestions[0].Suggestion;
        one.Action.Should().Be(SuggestedActionKind.AssignOwnFleet);
        one.SuggestedAction.Should().NotContain("warning");
    }

    [Fact]
    public async Task PlanTheList_TheOrderThatTipsTheDay_NamesTheOverCapacityWarningOnItsSuggestion()
    {
        var (first, second) = await TwoOrdersThatTipTheDayTogetherAsync(PlannerAgentCalls.Weekday(8));

        var run = await PlanTheListRun.ExecuteAsync(fixture, [first, second], maxOrders: 10, _cancellationToken);

        // With the first assumed, Own fleet is over capacity: the Suggestion names that warning, whether it sends the order to a Carrier or accepts the warning on Own fleet.
        run.Plan.Suggestions[1].Suggestion.SuggestedAction.Should().MatchRegex("Own fleet would carry a warning: .+|accepting this warning: .+");
    }

    [Fact]
    public async Task PlanTheList_AsksOneQuestionPerOrder_AndOnlyTheLaterOnesCarryTheTally()
    {
        var (first, second) = await TwoOrdersThatTipTheDayTogetherAsync(PlannerAgentCalls.Weekday(8));

        var run = await PlanTheListRun.ExecuteAsync(fixture, [first, second], maxOrders: 10, _cancellationToken);

        run.ToolCalls.Should().Equal([PlannerAgentCalls.AssignmentOptionsTool, PlannerAgentCalls.AssignmentOptionsAssumingTool]);
        run.Runs.Select(result => result.RunId).Should().OnlyHaveUniqueItems("each Suggestion is a run of its own");
    }

    [Fact]
    public async Task PlanTheList_AssignsNothing()
    {
        var (first, second) = await TwoOrdersThatTipTheDayTogetherAsync(PlannerAgentCalls.Weekday(8));

        await PlanTheListRun.ExecuteAsync(fixture, [first, second], maxOrders: 10, _cancellationToken);

        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        fixture.ShipmentsPlannedFor(first).Should().BeEmpty();
        fixture.ShipmentsPlannedFor(second).Should().BeEmpty();
    }

    [Fact]
    public async Task PlanTheList_ARunOverItsCap_ReturnsTheSuggestionsMadeSoFar_AndSaysWhereItStopped()
    {
        var orders = await ThreeSmallOrdersAsync(PlannerAgentCalls.Weekday(9));

        var run = await PlanTheListRun.ExecuteAsync(fixture, orders, maxOrders: 2, _cancellationToken);

        run.Plan.Suggestions.Select(listed => listed.TransportOrderNumber).Should().Equal(orders[0], orders[1]);
        run.Plan.Stopped.Should().Be(new PlanStop(PlanStopReason.CapReached, OrdersLeft: 1, NextOrder: orders[2]));
    }

    [Fact]
    public async Task PlanTheList_ARunOverItsCap_MakesNoMoreToolCallsThanTheCap()
    {
        var orders = await ThreeSmallOrdersAsync(PlannerAgentCalls.Weekday(9));

        var run = await PlanTheListRun.ExecuteAsync(fixture, orders, maxOrders: 2, _cancellationToken);

        run.ToolCalls.Should().Equal([PlannerAgentCalls.AssignmentOptionsTool, PlannerAgentCalls.AssignmentOptionsAssumingTool], "one read per order, the second carrying the first order as the tally");
    }

    private async Task<List<string>> ThreeSmallOrdersAsync(DateOnly day)
    {
        var orders = new List<string>();
        for (var n = 0; n < 3; n++)
        {
            orders.Add(await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(day).Weighing(100m), _cancellationToken));
        }

        return orders;
    }

    private static decimal DemandKgOf(JsonNode options) => options["ownFleet"]!["load"]!["demandKg"]!.GetValue<decimal>();

    private async Task<string> StatusOfAsync(string number) => (await fixture.GetTransportOrderAsync(number, _cancellationToken))["status"]!.GetValue<string>();

    /// <summary>Two orders on the day that each fit Own fleet alone and are over capacity together: a filler leaves room for 1.2 orders of 60% of the spare.</summary>
    private async Task<(string First, string Second)> TwoOrdersThatTipTheDayTogetherAsync(DateOnly day)
    {
        var before = (await LoadOfAsync(day))!;
        var spare = before["capacityKg"]!.GetValue<decimal>() - before["demandKg"]!.GetValue<decimal>();
        spare.Should().BeGreaterThan(1_000m, "the day must have room for two orders that fit alone, so the seed has not filled it");
        var weight = Math.Round(spare * 0.6m, 1);
        var first = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(day).Weighing(weight), _cancellationToken);
        var second = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(day).Weighing(weight), _cancellationToken);
        return (first, second);
    }

    private async Task<JsonNode?> LoadOfAsync(DateOnly day) =>
        (await fixture.ListOpenDaysAsync(_cancellationToken)).Single(open => CapacityCalls.DateOf(open) == day)["load"];
}
