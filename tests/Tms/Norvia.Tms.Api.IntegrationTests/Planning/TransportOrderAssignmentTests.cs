using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Planning;

/// <summary>
/// The Planner assigns an Accepted Transport Order to a Carrier or to Own fleet through the TMS API, as the TMS UI does
/// (P3). The TMS makes its one Planned Shipment, sets it to Planned and publishes ShipmentPlanned on the Shipment events
/// topic, all together. The assignment starts a Message trail of its own, so its event is found by the correlation ID
/// the TMS answers with, or by the Transport Order number.
/// </summary>
public sealed class TransportOrderAssignmentTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Assign_AcceptedOrderToACarrier_PlansOneShipment_AndPublishesOneShipmentPlannedWithTheCarrier()
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);

        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken);

        var shipmentNumber = assigned["shipmentNumber"]!.GetValue<string>();
        shipmentNumber.Should().MatchRegex("^SH-[0-9]{7}$");
        assigned["status"]!.GetValue<string>().Should().Be("Planned");
        var events = await tms.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken);
        var planned = events.Should().ContainSingle().Subject;
        var body = JsonNode.Parse(planned.Body.ToString())!;
        CanonicalModelSchemas.ValidateShipmentPlanned(body).IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(CanonicalModelSchemas.ValidateShipmentPlanned(body)));
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["transportOrderNumber"]!.GetValue<string>().Should().Be(number);
        body["shipperId"]!.GetValue<string>().Should().Be("SHP-0001");
        body["assignedTo"]!.GetValue<string>().Should().Be("Carrier");
        body["carrierId"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        planned.ApplicationProperties.Should().Contain("assignedTo", "Carrier");
        tms.ShipmentsPlannedFor(number).Should().ContainSingle();
    }

    [Fact]
    public async Task Assign_AcceptedOrderToOwnFleet_PlansOneShipment_AndPublishesOneShipmentPlannedForOwnFleet()
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);

        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToOwnFleet(), _cancellationToken);

        var events = await tms.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken);
        var planned = events.Should().ContainSingle().Subject;
        var body = JsonNode.Parse(planned.Body.ToString())!;
        CanonicalModelSchemas.ValidateShipmentPlanned(body).IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(CanonicalModelSchemas.ValidateShipmentPlanned(body)));
        body["assignedTo"]!.GetValue<string>().Should().Be("OwnFleet");
        body["carrierId"].Should().BeNull();
        planned.ApplicationProperties.Should().Contain("assignedTo", "OwnFleet");
        tms.ShipmentsPlannedFor(number).Should().ContainSingle();
    }

    [Fact]
    public async Task Assign_Always_PublishesOnANewTrail_WithTheTransportOrderNumber()
    {
        var intake = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), intake, _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);

        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken);

        var planning = assigned["correlationId"]!.GetValue<string>();
        planning.Should().NotBeNullOrWhiteSpace().And.NotBe(intake);
        var planned = (await tms.PublishedAsync(planning, "ShipmentPlanned", _cancellationToken)).Single();
        var body = JsonNode.Parse(planned.Body.ToString())!;
        body["correlationId"]!.GetValue<string>().Should().Be(planning);
        body["transportOrderNumber"]!.GetValue<string>().Should().Be(number);
        planned.CorrelationId.Should().Be(planning);
        planned.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["correlationId"] = planning,
            ["flow"] = "transport-planning",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = number,
            ["shipmentNumber"] = assigned["shipmentNumber"]!.GetValue<string>(),
        });
    }

    [Fact]
    public async Task Assign_AcceptedOrder_TheQuerySideShowsItPlannedWithItsShipment_AndTheShipmentWhoCarriesIt()
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);

        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken);

        var shipmentNumber = assigned["shipmentNumber"]!.GetValue<string>();
        var order = await tms.GetTransportOrderAsync(number, _cancellationToken);
        order["status"]!.GetValue<string>().Should().Be("Planned");
        order["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Planned");
        shipment["transportOrderNumber"]!.GetValue<string>().Should().Be(number);
        shipment["carriedBy"]!["assignedTo"]!.GetValue<string>().Should().Be("Carrier");
        shipment["carriedBy"]!["carrier"]!["id"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        shipment["carriedBy"]!["carrier"]!["name"]!.GetValue<string>().Should().Be("Proef Transport", "the test host loads its own Carriers");
    }

    [Fact]
    public async Task Assign_ToOwnFleet_TheShipmentQueryShowsOwnFleet()
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);

        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToOwnFleet(), _cancellationToken);

        var shipment = await tms.GetShipmentAsync(assigned["shipmentNumber"]!.GetValue<string>(), _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Planned");
        shipment["carriedBy"]!["assignedTo"]!.GetValue<string>().Should().Be("OwnFleet");
        shipment["carriedBy"]!["carrier"].Should().BeNull();
    }

    [Fact]
    public async Task Assign_TwoAssignmentsAtOnce_StoresAndPublishesOnlyOne_AndNothingOfTheOther()
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);

        var responses = await Task.WhenAll(
            tms.AssignAsync(number, PlanningCalls.ToCarrier(), _cancellationToken),
            tms.AssignAsync(number, PlanningCalls.ToOwnFleet(), _cancellationToken));

        try
        {
            responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
            var winner = JsonNode.Parse(await responses.Single(r => r.StatusCode == HttpStatusCode.Created).Content.ReadAsStringAsync(_cancellationToken))!;
            var shipmentNumber = winner["shipmentNumber"]!.GetValue<string>();
            await tms.PublishedAsync(winner["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken);

            tms.ShipmentsPlannedFor(number).Should().ContainSingle("the assignment that lost stored and published nothing")
                .Which.ApplicationProperties.Should().Contain("shipmentNumber", shipmentNumber);
            (await tms.GetTransportOrderAsync(number, _cancellationToken))["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
            (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["carriedBy"]!["assignedTo"]!.GetValue<string>()
                .Should().Be(winner["carriedBy"]!["assignedTo"]!.GetValue<string>());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Assign_SentAgainUnderItsKey_AnswersAsBefore_AndPublishesNothingMore()
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);
        var key = Guid.NewGuid().ToString();
        using var first = await tms.AssignAsync(number, PlanningCalls.ToCarrier(), _cancellationToken, key);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var planned = JsonNode.Parse(await first.Content.ReadAsStringAsync(_cancellationToken))!;
        await tms.PublishedAsync(planned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken);

        using var retry = await tms.AssignAsync(number, PlanningCalls.ToCarrier(), _cancellationToken, key);

        var body = await retry.Content.ReadAsStringAsync(_cancellationToken);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var repeat = JsonNode.Parse(body)!;
        repeat["shipmentNumber"]!.GetValue<string>().Should().Be(planned["shipmentNumber"]!.GetValue<string>());
        repeat["correlationId"]!.GetValue<string>().Should().Be(planned["correlationId"]!.GetValue<string>());
        repeat["repeat"]!.GetValue<bool>().Should().BeTrue();
        await Task.Delay(TimeSpan.FromSeconds(3), _cancellationToken); // Let the outbox run, so an event would show up.
        tms.ShipmentsPlannedFor(number).Should().ContainSingle("a retry plans nothing, so it publishes nothing");
    }

    [Fact]
    public async Task Assign_WithoutAnIdempotencyKey_IsABadRequest()
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);

        using var response = await tms.TmsUi.PostAsJsonAsync(
            new Uri($"/v1/transport-orders/{number}/assign", UriKind.Relative), PlanningCalls.ToOwnFleet(), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tms.GetTransportOrderAsync(number, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Accepted");
    }

    [Fact]
    public async Task Assign_ToACarrierWithoutNamingIt_IsABadRequest()
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);

        using var response = await tms.AssignAsync(number, new JsonObject { ["assignedTo"] = "Carrier" }, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tms.GetTransportOrderAsync(number, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Accepted");
    }

    [Fact]
    public async Task Assign_UnknownTransportOrder_IsNotFound()
    {
        using var response = await tms.AssignAsync("TO-9999999", PlanningCalls.ToOwnFleet(), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetShipment_UnknownShipment_IsNotFound()
    {
        using var response = await tms.Tms.GetAsync(new Uri("/v1/shipments/SH-9999999", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Assign_Always_TracesOnItsOwnTrailWithTheTrackingContractKeys()
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);

        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken);

        var planning = assigned["correlationId"]!.GetValue<string>();
        var log = await tms.TmsLogAsync(
            log => log["correlationId"] == planning && log.Text.Contains("planned as Shipment", StringComparison.Ordinal),
            _cancellationToken);
        log["flow"].Should().Be("transport-planning");
        log["partyId"].Should().Be("SHP-0001");
        log["transportOrderNumber"].Should().Be(number);
        log["shipmentNumber"].Should().Be(assigned["shipmentNumber"]!.GetValue<string>());
        log["outcome"].Should().Be("Succeeded");
    }
}
