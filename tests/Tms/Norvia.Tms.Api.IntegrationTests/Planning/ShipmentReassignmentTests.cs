using System.Net;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Planning;

/// <summary>
/// While a Shipment is Planned, the Planner reassigns it to Own fleet or another Carrier through the TMS API, as the TMS
/// UI does (P3). The Shipment keeps its Shipment number, records the change in its history of assignments and publishes
/// ShipmentReassigned on the Shipment events topic, on a Message trail of its own.
/// </summary>
public sealed class ShipmentReassignmentTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reassign_CarrierToCarrier_KeepsTheShipmentNumber_AndPublishesOneShipmentReassignedWithWhoCarriedItBefore()
    {
        var (number, shipmentNumber) = await PlannedAsync(PlanningCalls.ToCarrier());

        var reassigned = await tms.ReassignedAsync(shipmentNumber, PlanningCalls.ToCarrier(PlanningCalls.OtherCarrierId), _cancellationToken);

        reassigned["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        reassigned["status"]!.GetValue<string>().Should().Be("Planned");
        reassigned["carriedBy"]!["carrier"]!["id"]!.GetValue<string>().Should().Be(PlanningCalls.OtherCarrierId);
        reassigned["previouslyCarriedBy"]!["carrier"]!["id"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        var message = await SingleReassignedAsync(reassigned);
        var body = Valid(message);
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["transportOrderNumber"]!.GetValue<string>().Should().Be(number);
        body["shipperId"]!.GetValue<string>().Should().Be("SHP-0001");
        body["assignedTo"]!.GetValue<string>().Should().Be("Carrier");
        body["carrierId"]!.GetValue<string>().Should().Be(PlanningCalls.OtherCarrierId);
        body["previousAssignedTo"]!.GetValue<string>().Should().Be("Carrier");
        body["previousCarrierId"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        message.ApplicationProperties.Should().Contain("assignedTo", "Carrier");
        tms.ShipmentsReassigned(shipmentNumber).Should().ContainSingle();
        (await tms.GetTransportOrderAsync(number, _cancellationToken))["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
    }

    [Fact]
    public async Task Reassign_CarrierToOwnFleet_PublishesShipmentReassignedForOwnFleet()
    {
        var (_, shipmentNumber) = await PlannedAsync(PlanningCalls.ToCarrier());

        var reassigned = await tms.ReassignedAsync(shipmentNumber, PlanningCalls.ToOwnFleet(), _cancellationToken);

        var message = await SingleReassignedAsync(reassigned);
        var body = Valid(message);
        body["assignedTo"]!.GetValue<string>().Should().Be("OwnFleet");
        body["carrierId"].Should().BeNull();
        body["previousAssignedTo"]!.GetValue<string>().Should().Be("Carrier");
        message.ApplicationProperties.Should().Contain("assignedTo", "OwnFleet");
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["carriedBy"]!["assignedTo"]!.GetValue<string>().Should().Be("OwnFleet");
    }

    [Fact]
    public async Task Reassign_OwnFleetToCarrier_PublishesShipmentReassignedForTheCarrier()
    {
        var (_, shipmentNumber) = await PlannedAsync(PlanningCalls.ToOwnFleet());

        var reassigned = await tms.ReassignedAsync(shipmentNumber, PlanningCalls.ToCarrier(), _cancellationToken);

        var message = await SingleReassignedAsync(reassigned);
        var body = Valid(message);
        body["assignedTo"]!.GetValue<string>().Should().Be("Carrier");
        body["carrierId"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        body["previousAssignedTo"]!.GetValue<string>().Should().Be("OwnFleet");
        body["previousCarrierId"].Should().BeNull();
        message.ApplicationProperties.Should().Contain("assignedTo", "Carrier");
    }

    [Theory]
    [InlineData("CAR-UNKNOWN", "UnknownCarrier")]
    [InlineData(PlanningCalls.InactiveCarrierId, "InactiveCarrier")]
    public async Task Reassign_ToACarrierThatCantBeChosen_IsRefusedWithP32sCode_AndChangesNothing(string carrierId, string code)
    {
        var (_, shipmentNumber) = await PlannedAsync(PlanningCalls.ToCarrier());

        var problem = await tms.ReassignRefusedAsync(shipmentNumber, PlanningCalls.ToCarrier(carrierId), HttpStatusCode.UnprocessableEntity, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be(code);
        problem["detail"]!.GetValue<string>().Should().Contain(carrierId);
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["carriedBy"]!["carrier"]!["id"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        shipment["assignments"]!.AsArray().Should().ContainSingle();
        await Task.Delay(TimeSpan.FromSeconds(3), _cancellationToken); // Let the outbox run, so an event would show up.
        tms.ShipmentsReassigned(shipmentNumber).Should().BeEmpty("a refused reassignment publishes nothing");
    }

    [Fact]
    public async Task Reassign_ToWhoCarriesItAlready_IsRefusedAsUnchanged()
    {
        var (_, shipmentNumber) = await PlannedAsync(PlanningCalls.ToOwnFleet());

        var problem = await tms.ReassignRefusedAsync(shipmentNumber, PlanningCalls.ToOwnFleet(), HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("AssignmentUnchanged");
    }

    [Fact]
    public async Task Reassign_UnknownShipment_IsRefusedAsNotFound()
    {
        var problem = await tms.ReassignRefusedAsync("SH-9999999", PlanningCalls.ToOwnFleet(), HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotFound");
    }

    [Fact]
    public async Task Reassign_Twice_TheShipmentQueryShowsTheHistoryOfAssignmentsInOrder()
    {
        var (_, shipmentNumber) = await PlannedAsync(PlanningCalls.ToCarrier());
        var planning = (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["correlationId"]!.GetValue<string>();

        var first = await tms.ReassignedAsync(shipmentNumber, PlanningCalls.ToOwnFleet(), _cancellationToken);
        var second = await tms.ReassignedAsync(shipmentNumber, PlanningCalls.ToCarrier(PlanningCalls.OtherCarrierId), _cancellationToken);

        var assignments = (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["assignments"]!.AsArray();
        assignments.Select(a => a!["carriedBy"]!["carrier"]?["id"]?.GetValue<string>())
            .Should().Equal(PlanningCalls.CarrierId, null, PlanningCalls.OtherCarrierId);
        assignments.Select(a => a!["correlationId"]!.GetValue<string>())
            .Should().Equal(planning, first["correlationId"]!.GetValue<string>(), second["correlationId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Reassign_EachReassignment_PublishesOnItsOwnNewTrail_WithTheTransportOrderNumber()
    {
        var (number, shipmentNumber) = await PlannedAsync(PlanningCalls.ToCarrier());
        var planning = (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["correlationId"]!.GetValue<string>();

        var first = await tms.ReassignedAsync(shipmentNumber, PlanningCalls.ToOwnFleet(), _cancellationToken);
        var second = await tms.ReassignedAsync(shipmentNumber, PlanningCalls.ToCarrier(), _cancellationToken);

        var trails = new[] { first, second }.Select(r => r["correlationId"]!.GetValue<string>()).ToList();
        trails.Should().OnlyHaveUniqueItems().And.NotContain(planning);
        foreach (var trail in trails)
        {
            var message = (await tms.PublishedAsync(trail, "ShipmentReassigned", _cancellationToken)).Single();
            message.CorrelationId.Should().Be(trail);
            JsonNode.Parse(message.Body.ToString())!["correlationId"]!.GetValue<string>().Should().Be(trail);
            message.ApplicationProperties.Should().Contain(new Dictionary<string, object>
            {
                ["correlationId"] = trail,
                ["flow"] = "transport-planning",
                ["partyId"] = "SHP-0001",
                ["transportOrderNumber"] = number,
                ["shipmentNumber"] = shipmentNumber,
            });
        }
    }

    [Fact]
    public async Task Reassign_Always_TracesOnItsOwnTrailWithTheTrackingContractKeys()
    {
        var (number, shipmentNumber) = await PlannedAsync(PlanningCalls.ToCarrier());

        var reassigned = await tms.ReassignedAsync(shipmentNumber, PlanningCalls.ToOwnFleet(), _cancellationToken);

        var trail = reassigned["correlationId"]!.GetValue<string>();
        var log = await tms.TmsLogAsync(
            log => log["correlationId"] == trail && log.Text.Contains("reassigned from", StringComparison.Ordinal),
            _cancellationToken);
        log["flow"].Should().Be("transport-planning");
        log["partyId"].Should().Be("SHP-0001");
        log["transportOrderNumber"].Should().Be(number);
        log["shipmentNumber"].Should().Be(shipmentNumber);
        log["outcome"].Should().Be("Succeeded");
    }

    [Fact]
    public async Task Reassign_SentAgainUnderItsKey_AnswersAsBefore_AndPublishesNothingMore()
    {
        var (_, shipmentNumber) = await PlannedAsync(PlanningCalls.ToCarrier());
        var key = Guid.NewGuid().ToString();
        using var first = await tms.ReassignAsync(shipmentNumber, PlanningCalls.ToOwnFleet(), _cancellationToken, key);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var reassigned = JsonNode.Parse(await first.Content.ReadAsStringAsync(_cancellationToken))!;
        await tms.PublishedAsync(reassigned["correlationId"]!.GetValue<string>(), "ShipmentReassigned", _cancellationToken);

        using var retry = await tms.ReassignAsync(shipmentNumber, PlanningCalls.ToOwnFleet(), _cancellationToken, key);

        var body = await retry.Content.ReadAsStringAsync(_cancellationToken);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var repeat = JsonNode.Parse(body)!;
        repeat["correlationId"]!.GetValue<string>().Should().Be(reassigned["correlationId"]!.GetValue<string>());
        repeat["repeat"]!.GetValue<bool>().Should().BeTrue();
        await Task.Delay(TimeSpan.FromSeconds(3), _cancellationToken); // Let the outbox run, so an event would show up.
        tms.ShipmentsReassigned(shipmentNumber).Should().ContainSingle("a retry reassigns nothing, so it publishes nothing");
    }

    [Fact]
    public async Task Reassign_ToACarrierWithoutNamingIt_IsABadRequest()
    {
        var (_, shipmentNumber) = await PlannedAsync(PlanningCalls.ToOwnFleet());

        using var response = await tms.ReassignAsync(shipmentNumber, new JsonObject { ["assignedTo"] = "Carrier" }, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>A Transport Order the Planner assigned, whose ShipmentPlanned is out, so later events are the reassignments'.</summary>
    private async Task<(string Number, string ShipmentNumber)> PlannedAsync(JsonObject assignment)
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);
        var assigned = await tms.AssignPlannedAsync(number, assignment, _cancellationToken);
        await tms.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken);
        return (number, assigned["shipmentNumber"]!.GetValue<string>());
    }

    private async Task<ServiceBusReceivedMessage> SingleReassignedAsync(JsonNode reassigned)
    {
        var events = await tms.PublishedAsync(reassigned["correlationId"]!.GetValue<string>(), "ShipmentReassigned", _cancellationToken);
        return events.Should().ContainSingle().Subject;
    }

    private static JsonNode Valid(ServiceBusReceivedMessage message)
    {
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateShipmentReassigned(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        return body;
    }
}
