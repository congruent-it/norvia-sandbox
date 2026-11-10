using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.TestSupport.Capacity;

namespace Norvia.Tms.Api.IntegrationTests.PlannerAgent;

/// <summary>
/// <c>get_assignment_options</c> through the stand-in to the TMS API's planner agent view, for a Transport Order each test submits itself. What it says about Own fleet and about each
/// active Carrier is what P3 (the assignment and its rules), P10 (the load check) and P9 (the Carrier cost at release) give for the same order, which each test shows by doing what the Planner
/// does next. A test that assigns to Own fleet collects on a day of its own, so no other test changes the day's load. The view is an allowlist: no street address, no Consignee.
/// The test seed prices Proef Transport's Rotterdam to Utrecht Lane at 9.50 per 100 kg up to 2,000 kg; Reserve Transport has no Carrier Rate Card; Gesloten Vracht is inactive.
/// </summary>
public sealed class AssignmentOptionsTests(PlannerAgentFixture fixture)
{
    // Every property the agent view may carry (PlannerAgentMapping.cs); the answer's JSON names no other.
    private static readonly HashSet<string> Allowed =
    [
        "transportOrderNumber", "status", "shipper", "ratingResult", "asOf", "collection", "delivery", "collectionWindow", "deliveryWindow", "goods", "ownFleet", "carriers",
        "city", "postcodeArea", "country", "earliest", "latest", "totalWeightKg", "palletPlaces",
        "meetsAssignmentRules", "ruleBroken", "fit", "collectionDay", "warning", "load", "availableVehicles", "availableDrivers", "driversWithDrivingHoursLeft", "mostDrivingHoursLeft",
        "capacityKg", "capacityPalletPlaces", "demandKg", "demandPalletPlaces", "over",
        "carrierId", "name", "carrierCost", "notPriced",
    ];

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnOrderThatFitsOwnFleet_IsReadAsFitting_WithNoWarning_AsTheLoadCheckSaysOnceItIsAssigned()
    {
        var day = PlannerAgentCalls.Weekday(0);
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(day), _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var options = await client.OptionsOfAsync(number, _cancellationToken);
        var (_, assigned) = await PlannerAgentCalls.PlannedAsync(fixture, number, PlanningCalls.ToOwnFleet(), _cancellationToken);

        // P3: the Assignment rules let it through, and P10: the load check of the collection day with the order counted gives no warning.
        var own = options["ownFleet"]!;
        own["meetsAssignmentRules"]!.GetValue<bool>().Should().BeTrue();
        own["ruleBroken"].Should().BeNull();
        own["fit"]!.GetValue<string>().Should().Be("Fits");
        own["warning"].Should().BeNull();
        own["collectionDay"]!.GetValue<string>().Should().Be($"{day:yyyy-MM-dd}");
        own["availableVehicles"]!.GetValue<int>().Should().BePositive();
        own["driversWithDrivingHoursLeft"]!.GetValue<int>().Should().BePositive();
        assigned["loadWarning"].Should().BeNull("the Planner sees no warning on assigning it");
        var load = (await LoadOfAsync(day))!;
        own["load"]!["over"]!.GetValue<bool>().Should().BeFalse().And.Be(load["over"]!.GetValue<bool>());
        own["load"]!["demandKg"]!.GetValue<decimal>().Should().Be(load["demandKg"]!.GetValue<decimal>(), "the options counted the order the way the day's load now does");
        own["load"]!["capacityKg"]!.GetValue<decimal>().Should().Be(load["capacityKg"]!.GetValue<decimal>());
        options["goods"]!["totalWeightKg"]!.GetValue<decimal>().Should().Be(910.5m);
    }

    [Fact]
    public async Task AnOrderOnADayThatWouldBeOverCapacity_IsReadWithTheWarningTheAssignmentShows_AndOwnFleetStillMeetsTheRules()
    {
        var day = PlannerAgentCalls.Weekday(1);
        var before = (await LoadOfAsync(day))!;
        var spare = before["capacityKg"]!.GetValue<decimal>() - before["demandKg"]!.GetValue<decimal>();
        var filler = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(day).Weighing(spare - 500m), _cancellationToken);
        await PlannerAgentCalls.PlannedAsync(fixture, filler, PlanningCalls.ToOwnFleet(), _cancellationToken);
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(day).Weighing(1_500m), _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var options = await client.OptionsOfAsync(number, _cancellationToken);
        var (_, assigned) = await PlannerAgentCalls.PlannedAsync(fixture, number, PlanningCalls.ToOwnFleet(), _cancellationToken);

        var own = options["ownFleet"]!;
        own["fit"]!.GetValue<string>().Should().Be("OverCapacity");
        own["meetsAssignmentRules"]!.GetValue<bool>().Should().BeTrue("Over capacity is a warning and never blocks the assignment");
        own["load"]!["over"]!.GetValue<bool>().Should().BeTrue();
        own["load"]!["demandKg"]!.GetValue<decimal>().Should().Be(before["demandKg"]!.GetValue<decimal>() + spare - 500m + 1_500m);
        own["warning"]!.GetValue<string>().Should().Be(assigned["loadWarning"]!["text"]!.GetValue<string>(), "it is the warning P3 shows on assigning");
        options.CarrierOf(PlanningCalls.CarrierId)["carrierCost"]!.GetValue<decimal>().Should().BePositive("a Carrier can still carry it");
        options.CarrierOf(PlanningCalls.CarrierId)["meetsAssignmentRules"]!.GetValue<bool>().Should().BeTrue();
        (await LoadOfAsync(day))!["over"]!.GetValue<bool>().Should().Be(own["load"]!["over"]!.GetValue<bool>(), "P10 shows the day Over capacity once it is assigned");
    }

    [Fact]
    public async Task AnOrderCollectedBeyondThePlanningHorizon_IsReadWithTheOutOfHorizonWarning_TheAssignmentShows()
    {
        var booking = Bookings.Valid(TmsCalls.NewReference()).CollectedFrom(new DateTimeOffset(2050, 3, 1, 8, 0, 0, TimeSpan.Zero));
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, booking, _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var options = await client.OptionsOfAsync(number, _cancellationToken);
        var (_, assigned) = await PlannerAgentCalls.PlannedAsync(fixture, number, PlanningCalls.ToOwnFleet(), _cancellationToken);

        var own = options["ownFleet"]!;
        own["fit"]!.GetValue<string>().Should().Be("OutsideHorizon");
        own["meetsAssignmentRules"]!.GetValue<bool>().Should().BeTrue("a warning never blocks");
        own["load"].Should().BeNull("there is no load check beyond the horizon");
        own["warning"]!.GetValue<string>().Should().Be(assigned["loadWarning"]!["text"]!.GetValue<string>()).And.Contain("Outside the Planning horizon");
        options.CarrierOf(PlanningCalls.CarrierId)["carrierCost"].Should().BeNull();
        options.CarrierOf(PlanningCalls.CarrierId)["notPriced"]!.GetValue<string>().Should().Be("NoValidRateCardVersion");
    }

    [Fact]
    public async Task ACarrierCost_IsTheOneTheReleasePricesItAt_AndACarrierWithoutARateCardIsReadAsNotPriced()
    {
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(2)), _cancellationToken);
        var other = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(2)), _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var options = await client.OptionsOfAsync(number, _cancellationToken);
        var otherOptions = await client.OptionsOfAsync(other, _cancellationToken);
        var (shipmentNumber, _) = await PlannerAgentCalls.PlannedAsync(fixture, number, PlanningCalls.ToCarrier(), _cancellationToken);
        var released = await fixture.ReleasedAsync(shipmentNumber, _cancellationToken);
        var (otherShipment, _) = await PlannerAgentCalls.PlannedAsync(fixture, other, PlanningCalls.ToCarrier(PlanningCalls.OtherCarrierId), _cancellationToken);
        var refusal = await fixture.ReleaseRefusedAsync(otherShipment, HttpStatusCode.UnprocessableEntity, _cancellationToken);

        // P9: 910.5 kg ÷ 100 × 9.50 = 86.4975, so 86.50, which the release freezes on the Carrier booking and publishes.
        var priced = options.CarrierOf(PlanningCalls.CarrierId);
        priced["name"]!.GetValue<string>().Should().Be("Proef Transport");
        priced["meetsAssignmentRules"]!.GetValue<bool>().Should().BeTrue();
        priced["notPriced"].Should().BeNull();
        var events = await fixture.PublishedAsync(released["correlationId"]!.GetValue<string>(), "ShipmentReleased", _cancellationToken);
        var cost = JsonNode.Parse(events.Should().ContainSingle().Subject.Body.ToString())!["carrierCost"]!["total"]!.GetValue<decimal>();
        priced["carrierCost"]!.GetValue<decimal>().Should().Be(86.50m).And.Be(cost);
        var unpriced = otherOptions.CarrierOf(PlanningCalls.OtherCarrierId);
        unpriced["carrierCost"].Should().BeNull();
        unpriced["notPriced"]!.GetValue<string>().Should().Be("NoRateCard").And.Be(refusal["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task ACarrierThatHasNoLaneForTheOrder_IsReadAsNoMatchingLane_AsTheReleaseRefuses()
    {
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(3)).DeliveredTo("9711 AA"), _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var options = await client.OptionsOfAsync(number, _cancellationToken);
        var (shipmentNumber, _) = await PlannerAgentCalls.PlannedAsync(fixture, number, PlanningCalls.ToCarrier(), _cancellationToken);
        var refusal = await fixture.ReleaseRefusedAsync(shipmentNumber, HttpStatusCode.UnprocessableEntity, _cancellationToken);

        var carrier = options.CarrierOf(PlanningCalls.CarrierId);
        carrier["carrierCost"].Should().BeNull();
        carrier["notPriced"]!.GetValue<string>().Should().Be("NoMatchingLane").And.Be(refusal["code"]!.GetValue<string>());
        carrier["meetsAssignmentRules"]!.GetValue<bool>().Should().BeTrue("a Carrier that cannot be priced is still a Carrier the order may be assigned to");
    }

    [Fact]
    public async Task OnlyTheActiveCarriers_AreOptions_AsTheAssignmentRulesHaveIt()
    {
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(4)), _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var options = await client.OptionsOfAsync(number, _cancellationToken);
        var refusal = await fixture.AssignRefusedAsync(number, PlanningCalls.ToCarrier(PlanningCalls.InactiveCarrierId), HttpStatusCode.UnprocessableEntity, _cancellationToken);

        options["carriers"]!.AsArray().Select(carrier => carrier!["carrierId"]!.GetValue<string>())
            .Should().BeEquivalentTo([PlanningCalls.CarrierId, PlanningCalls.OtherCarrierId], "Gesloten Vracht is inactive, so P3 refuses an assignment to it");
        refusal["code"]!.GetValue<string>().Should().Be("InactiveCarrier");
    }

    [Fact]
    public async Task AnOrder_IsReadNarrowed_WithNoStreetAddressAndNoConsignee()
    {
        var booking = PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(0));
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, booking, _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (isError, text) = await client.GetAssignmentOptionsAsync(number, _cancellationToken);

        isError.Should().BeFalse(text);
        text.Should().NotContainAny(PlannerAgentCalls.NeverForTheAgent).And.NotContain(booking["shipperReference"]!.GetValue<string>());
        var options = JsonNode.Parse(text)!;
        PropertyNames(options).Should().BeSubsetOf(Allowed);
        options["shipper"]!.GetValue<string>().Should().Be("Brouwer Dranken");
        options["ratingResult"]!.GetValue<string>().Should().Be("Rated");
        options["collection"]!.AsObject().ToDictionary(property => property.Key, property => property.Value!.GetValue<string>())
            .Should().Equal(new Dictionary<string, string> { ["city"] = "Rotterdam", ["postcodeArea"] = "30", ["country"] = "NL" });
        options["delivery"]!.AsObject().ToDictionary(property => property.Key, property => property.Value!.GetValue<string>())
            .Should().Equal(new Dictionary<string, string> { ["city"] = "Utrecht", ["postcodeArea"] = "35", ["country"] = "NL" });
        options["goods"]!["palletPlaces"]!.GetValue<int>().Should().BePositive();
    }

    [Fact]
    public async Task ATransportOrderThatDoesNotExist_ReturnsAnError()
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (isError, _) = await client.GetAssignmentOptionsAsync("TO-9999999", _cancellationToken);

        isError.Should().BeTrue();
    }

    [Fact]
    public async Task TheTool_ReadsAndNeverWrites_SoReadingTwiceChangesNothing()
    {
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(0)), _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var first = await client.OptionsOfAsync(number, _cancellationToken);
        var second = await client.OptionsOfAsync(number, _cancellationToken);

        first["status"]!.GetValue<string>().Should().Be("Accepted").And.Be(second["status"]!.GetValue<string>(), "reading plans nothing");
        (await fixture.Tms.GetStringAsync(new Uri($"/v1/transport-orders/{number}", UriKind.Relative), _cancellationToken)).Should().Contain("\"Accepted\"");
    }

    /// <summary>The day's load as P10 shows it to the Planner, from the open days.</summary>
    private async Task<JsonNode?> LoadOfAsync(DateOnly day) =>
        (await fixture.ListOpenDaysAsync(_cancellationToken)).Single(open => CapacityCalls.DateOf(open) == day)["load"];

    private static IEnumerable<string> PropertyNames(JsonNode? node) => node switch
    {
        JsonObject properties => properties.SelectMany(property => PropertyNames(property.Value).Prepend(property.Key)),
        JsonArray items => items.SelectMany(PropertyNames),
        _ => [],
    };
}
