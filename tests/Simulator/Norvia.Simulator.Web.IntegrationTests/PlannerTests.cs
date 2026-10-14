extern alias SimulatorWeb;

using System.Text.Json.Nodes;
using Norvia.Simulator.Application.Plans.Preview;
using Norvia.Simulator.Application.Runs;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

[Collection("Simulator runs")]
public sealed class PlannerTests(SimulatorFixture fixture)
{
    private static readonly string[] ActiveCarriers = ["CAR-0001", "CAR-0002", "CAR-0003"];

    // Havenstad Koeriers: the seed file has it inactive, and the TMS refuses an assignment to it.
    private const string InactiveCarrier = "CAR-0004";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AcceptedTransportOrders_BecomePlannedShipments_CarriedByCarriersWhenNoneIsMeantForOwnFleet()
    {
        var settings = Runs.Planned(orders: 20, Runs.NextSeed(), assignedPercent: 100, ownFleetPercent: 0, reassignPercent: 0, badSharePercent: 20);

        var (preview, run, log) = await RunAsync(fixture.Simulator, settings);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var accepted = log.Where(line => line.Outcome == LogOutcome.Accepted).Select(line => line.TransportOrderNumber).ToList();
        var planned = log.Where(line => line.Outcome == LogOutcome.Planned).ToList();
        preview.Planner.Assigned.Should().Be(accepted.Count);
        planned.Should().HaveCount(accepted.Count);
        run.Assigned.Should().Be(planned.Count);
        planned.Select(line => line.TransportOrderNumber).Should().BeEquivalentTo(accepted);
        foreach (var line in planned)
        {
            var shipment = await ShipmentOf(line);
            shipment["status"]!.GetValue<string>().Should().Be("Planned");
            shipment["transportOrderNumber"]!.GetValue<string>().Should().Be(line.TransportOrderNumber);
            CarrierOf(shipment).Should().BeOneOf(ActiveCarriers);
        }
    }

    [Fact]
    public async Task TheShareAssigned_IsExactlyThatShareOfTheOrdersNorviaAccepts_AndTheRestStayWaitingForPlanning()
    {
        var settings = Runs.Planned(orders: 20, Runs.NextSeed(), assignedPercent: 50, ownFleetPercent: 0, reassignPercent: 0);

        var (preview, run, log) = await RunAsync(fixture.Simulator, settings);

        var accepted = log.Where(line => line.Outcome == LogOutcome.Accepted).ToList();
        var planned = log.Where(line => line.Outcome == LogOutcome.Planned).ToList();
        accepted.Should().HaveCount(20);
        planned.Should().HaveCount(10);
        preview.Planner.Assigned.Should().Be(10);
        run.Assigned.Should().Be(10);
        var plannedOrders = planned.Select(line => line.TransportOrderNumber).ToHashSet();
        foreach (var row in preview.Rows)
        {
            var order = accepted.Single(line => line.ShipperReference == row.Reference).TransportOrderNumber;
            plannedOrders.Contains(order).Should().Be(row.Planner is not null, $"{row.Reference} is {(row.Planner is null ? "not " : "")}meant to be assigned");
        }

        var waiting = await fixture.ShipperReferencesAsync("Accepted", Ct);
        waiting.Should().Contain(preview.Rows.Where(row => row.Planner is null).Select(row => row.Reference));
    }

    [Fact]
    public async Task OrdersMeantForOwnFleet_GoThereOnlyWhereTheyFit_AndTheOthersAreCarriedByCarriers()
    {
        var settings = Runs.Planned(orders: 20, Runs.NextSeed(), assignedPercent: 100, ownFleetPercent: 50, reassignPercent: 0);

        var (preview, _, log) = await RunAsync(fixture.Simulator, settings);

        var planned = log.Where(line => line.Outcome == LogOutcome.Planned).ToList();
        planned.Should().HaveCount(20);
        var ownFleet = new List<string>();
        foreach (var row in preview.Rows)
        {
            var line = planned.Single(candidate => candidate.ShipperReference == row.Reference);
            var carrier = CarrierOf(await ShipmentOf(line));
            if (carrier is null)
            {
                row.Planner.Should().Be(PlannerIntent.OwnFleetIfItFits, $"{row.Reference} went to Own fleet, which only orders meant for it do");
                ownFleet.Add(row.Reference);
            }
            else
            {
                carrier.Should().BeOneOf(ActiveCarriers, $"{row.Reference} goes to an active Carrier when it is not on Own fleet");
            }
        }

        // The Own fleet's room is the TMS's state, which the tests don't own: when it has none the whole plan goes to Carriers, as it should.
        using var response = await fixture.Tms.GetAsync(new Uri("/v1/capacity/days", UriKind.Relative), Ct);
        var days = response.IsSuccessStatusCode ? System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!.AsArray() : [];
        if (days.Count == 0)
        {
            ownFleet.Should().BeEmpty("the TMS has no Own fleet capacity to send an order to");
        }
    }

    [Fact]
    public async Task Reassignments_MoveTheShipmentToWhoDidNotCarryItBefore_AndKeepItsNumber()
    {
        var settings = Runs.Planned(orders: 8, Runs.NextSeed(), assignedPercent: 100, ownFleetPercent: 0, reassignPercent: 100);

        var (preview, _, log) = await RunAsync(fixture.Simulator, settings);

        var reassigned = log.Where(line => line.Outcome == LogOutcome.Reassigned).ToList();
        preview.Planner.Reassigned.Should().Be(8);
        reassigned.Should().HaveCount(8);
        foreach (var line in reassigned)
        {
            line.Action.Should().Be(PlannerActions.Reassign);
            log.Should().ContainSingle(planned => planned.Outcome == LogOutcome.Planned && planned.ShipmentNumber == line.ShipmentNumber
                && planned.TransportOrderNumber == line.TransportOrderNumber);
            var shipment = await ShipmentOf(line);
            var assignments = shipment["assignments"]!.AsArray().Select(assignment => assignment!["carriedBy"]!["carrier"]?["id"]?.GetValue<string>()).ToList();
            assignments.Should().HaveCount(2);
            assignments[1].Should().NotBe(assignments[0]);
            CarrierOf(shipment).Should().Be(assignments[1]);
        }
    }

    [Fact]
    public async Task ARefusalByTheTms_IsLoggedWithItsCodeAndCorrelationId_AndTheRunGoesOn()
    {
        await using var simulator = fixture.StartSimulatorWithActiveCarriers(InactiveCarrier);
        var settings = Runs.Planned(orders: 6, Runs.NextSeed(), assignedPercent: 100, ownFleetPercent: 0, reassignPercent: 0);

        var (_, run, log) = await RunAsync(simulator, settings);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        run.Assigned.Should().Be(0);
        var refused = log.Where(line => line.Outcome == LogOutcome.PlanningRefused).ToList();
        refused.Should().HaveCount(6, "a refusal does not stop the run: every order was tried");
        refused.Should().OnlyContain(line => line.Action == PlannerActions.Assign
            && !string.IsNullOrWhiteSpace(line.CorrelationId)
            && line.TransportOrderNumber != null
            && line.ShipmentNumber == null
            && line.Detail != null && line.Detail.Contains("InactiveCarrier", StringComparison.Ordinal));
        refused.Select(line => line.CorrelationId).Should().OnlyHaveUniqueItems();
        log.Where(line => line.Outcome == LogOutcome.Planned).Should().BeEmpty();
    }

    [Fact]
    public async Task EveryLineOfThePlanner_NamesTheTransportOrderAndTheShipmentItConcerns_AndTheCorrelationIdOfTheTmsCall()
    {
        var settings = Runs.Planned(orders: 6, Runs.NextSeed(), assignedPercent: 100, ownFleetPercent: 0, reassignPercent: 50);

        var (_, run, log) = await RunAsync(fixture.Simulator, settings);

        var planning = log.Where(Runs.IsPlanning).ToList();
        planning.Should().NotBeEmpty();
        planning.Should().OnlyContain(line => !string.IsNullOrWhiteSpace(line.CorrelationId)
            && !string.IsNullOrWhiteSpace(line.TransportOrderNumber) && !string.IsNullOrWhiteSpace(line.ShipmentNumber) && line.Actor == "Planner");
        planning.Select(line => line.CorrelationId).Should().OnlyHaveUniqueItems();
        foreach (var line in planning)
        {
            var shipment = await ShipmentOf(line);
            shipment["assignments"]!.AsArray().Select(assignment => assignment!["correlationId"]!.GetValue<string>())
                .Should().Contain(line.CorrelationId, "the log's correlation ID is the one the TMS traced the call on");
        }

        var byShipment = await fixture.Simulator.Requests.ListLogAsync(
            new SimulationRunId(run.Id), new LogFilter(Text: planning[0].ShipmentNumber), Ct);
        byShipment.Should().NotBeEmpty().And.OnlyContain(line => line.ShipmentNumber == planning[0].ShipmentNumber);
    }

    [Fact]
    public async Task TheSameSeedTwice_MakesTheSamePlannerChoices()
    {
        var settings = Runs.Planned(orders: 16, Runs.NextSeed(), assignedPercent: 75, ownFleetPercent: 0, reassignPercent: 30);

        var (_, _, first) = await RunAsync(fixture.Simulator, settings);
        var (_, _, second) = await RunAsync(fixture.Simulator, settings);

        var firstChoices = Choices(first);
        firstChoices.Should().NotBeEmpty();
        Choices(second).Should().Equal(firstChoices);
    }

    [Fact]
    public async Task AnotherSeed_MakesOtherPlannerChoices()
    {
        var seed = Runs.NextSeed();

        var (_, _, first) = await RunAsync(fixture.Simulator, Runs.Planned(24, seed, assignedPercent: 75, ownFleetPercent: 0, reassignPercent: 30));
        var (_, _, second) = await RunAsync(fixture.Simulator, Runs.Planned(24, seed + 1, assignedPercent: 75, ownFleetPercent: 0, reassignPercent: 30));

        Choices(second).Should().NotEqual(Choices(first));
    }

    [Fact]
    public async Task APlanPreview_PromisesWhatThePlannerDoes()
    {
        var settings = Runs.Planned(orders: 20, Runs.NextSeed(), assignedPercent: 60, ownFleetPercent: 0, reassignPercent: 50, badSharePercent: 25);

        var (preview, run, log) = await RunAsync(fixture.Simulator, settings);

        preview.Planner.Assigned.Should().Be(log.Count(line => line.Outcome == LogOutcome.Planned));
        preview.Planner.Reassigned.Should().Be(log.Count(line => line.Outcome == LogOutcome.Reassigned));
        run.ToAssign.Should().Be(preview.Planner.Assigned);
        run.Assigned.Should().Be(preview.Planner.Assigned);
    }

    private static List<(string Action, LogOutcome Outcome, string? Detail)> Choices(IReadOnlyList<LogLine> log) =>
        [.. log.Where(Runs.IsPlanning).OrderBy(line => line.Sequence).Select(line => (line.Action, line.Outcome, line.Detail))];

    private static async Task<(PlanPreview Preview, RunSummary Run, IReadOnlyList<LogLine> Log)> RunAsync(SimulatorHost simulator, SimulationSettings settings)
    {
        var preview = (await simulator.Requests.PreviewAsync(settings, Ct)).Value;
        var started = await simulator.Requests.StartAsync(settings, Ct);
        var run = await simulator.Requests.FinishedAsync(started.Id, Ct);
        return (preview, run, await simulator.Requests.LogAsync(run.Id, Ct));
    }

    private async Task<JsonObject> ShipmentOf(LogLine line) =>
        await fixture.ShipmentAsync(line.ShipmentNumber!, Ct) ?? throw new InvalidOperationException($"The TMS has no Shipment {line.ShipmentNumber}.");

    private static string? CarrierOf(JsonObject shipment) => shipment["carriedBy"]!["carrier"]?["id"]?.GetValue<string>();
}
