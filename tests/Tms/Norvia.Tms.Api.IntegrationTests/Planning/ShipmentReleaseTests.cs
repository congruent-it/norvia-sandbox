using System.Net;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Planning;

/// <summary>
/// The Planner hands a Planned Shipment over by releasing it through the TMS API, as the TMS UI does (P3). The Shipment
/// becomes Released and ShipmentReleased is published on the Shipment events topic with its Transport Order's details, on
/// a Message trail of its own. After that, P3 can no longer change it.
/// </summary>
public sealed class ShipmentReleaseTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Release_PlannedCarrierShipment_MakesItReleased_AndPublishesOneShipmentReleasedWithTheTransportOrdersDetails()
    {
        var (number, shipmentNumber) = await PlannedAsync(PlanningCalls.ToCarrier());

        var released = await tms.ReleasedAsync(shipmentNumber, _cancellationToken);

        released["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        released["status"]!.GetValue<string>().Should().Be("Released");
        released["carriedBy"]!["carrier"]!["id"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        var carrierBookingNumber = released["carrierBookingNumber"]!.GetValue<string>();
        carrierBookingNumber.Should().MatchRegex("^CB-[0-9]{7}$");
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Released");
        var bookings = shipment["carrierBookings"]!.AsArray();
        bookings.Should().ContainSingle();
        bookings[0]!["carrierBookingNumber"]!.GetValue<string>().Should().Be(carrierBookingNumber);
        bookings[0]!["status"]!.GetValue<string>().Should().Be("Requested");
        var message = await SingleReleasedAsync(released);
        var body = Valid(message);
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["transportOrderNumber"]!.GetValue<string>().Should().Be(number);
        body["shipperId"]!.GetValue<string>().Should().Be("SHP-0001");
        body["assignedTo"]!.GetValue<string>().Should().Be("Carrier");
        body["carrierId"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        body["carrierBookingNumber"]!.GetValue<string>().Should().Be(carrierBookingNumber);
        message.ApplicationProperties.Should().Contain("assignedTo", "Carrier");
        message.ApplicationProperties.Should().Contain("carrierBookingNumber", carrierBookingNumber);

        var order = body["transportOrder"]!;
        order["transportOrderNumber"]!.GetValue<string>().Should().Be(number);
        order["collection"]!["address"]!["city"]!.GetValue<string>().Should().Be("Rotterdam");
        order["collection"]!["window"]!["earliest"].Should().NotBeNull();
        order["delivery"]!["address"]!["city"]!.GetValue<string>().Should().Be("Utrecht");
        order["delivery"]!["consignee"]!["name"]!.GetValue<string>().Should().Be("Café De Zwaan");
        order["delivery"]!["window"]!["latest"].Should().NotBeNull();
        order["goodsLines"]!.AsArray().Select(line => line!["packageType"]!.GetValue<string>()).Should().Equal("pallet", "crate");
        tms.ShipmentsReleased(shipmentNumber).Should().ContainSingle();
    }

    [Fact]
    public async Task Release_PlannedOwnFleetShipment_PublishesShipmentReleasedForOwnFleet_AndCreatesNoCarrierBooking()
    {
        var (_, shipmentNumber) = await PlannedAsync(PlanningCalls.ToOwnFleet());

        var released = await tms.ReleasedAsync(shipmentNumber, _cancellationToken);

        released["carrierBookingNumber"].Should().BeNull();
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["carrierBookings"]!.AsArray().Should().BeEmpty();
        var message = await SingleReleasedAsync(released);
        var body = Valid(message);
        body["assignedTo"]!.GetValue<string>().Should().Be("OwnFleet");
        body["carrierId"].Should().BeNull();
        body["carrierBookingNumber"].Should().BeNull();
        message.ApplicationProperties.Should().Contain("assignedTo", "OwnFleet");
        message.ApplicationProperties.Should().NotContainKey("carrierBookingNumber");
    }

    [Fact]
    public async Task Release_ReleasedShipment_IsRefusedAsNotPlanned_AndPublishesNothingMore()
    {
        var shipmentNumber = await ReleasedAsync();

        var problem = await tms.ReleaseRefusedAsync(shipmentNumber, HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotPlanned");
        problem["detail"]!.GetValue<string>().Should().Contain(shipmentNumber);
        await Task.Delay(TimeSpan.FromSeconds(3), _cancellationToken); // Let the outbox run, so an event would show up.
        tms.ShipmentsReleased(shipmentNumber).Should().ContainSingle("a refused release publishes nothing");
    }

    [Fact]
    public async Task Reassign_ReleasedShipment_IsRefusedAsNotPlanned_AndChangesAndPublishesNothing()
    {
        var shipmentNumber = await ReleasedAsync();

        var problem = await tms.ReassignRefusedAsync(shipmentNumber, PlanningCalls.ToOwnFleet(), HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotPlanned");
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Released");
        shipment["carriedBy"]!["carrier"]!["id"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        shipment["assignments"]!.AsArray().Should().ContainSingle();
        await Task.Delay(TimeSpan.FromSeconds(3), _cancellationToken); // Let the outbox run, so an event would show up.
        tms.ShipmentsReassigned(shipmentNumber).Should().BeEmpty("a refused reassignment publishes nothing");
    }

    [Fact]
    public async Task Release_UnknownShipment_IsRefusedAsNotFound()
    {
        var problem = await tms.ReleaseRefusedAsync("SH-9999999", HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotFound");
    }

    [Fact]
    public async Task Release_EachRelease_PublishesOnItsOwnNewTrail_WithTheTransportOrderNumber()
    {
        var (number, shipmentNumber) = await PlannedAsync(PlanningCalls.ToCarrier());
        var planning = (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["correlationId"]!.GetValue<string>();
        var (_, otherShipmentNumber) = await PlannedAsync(PlanningCalls.ToCarrier());

        var released = await tms.ReleasedAsync(shipmentNumber, _cancellationToken);
        var other = await tms.ReleasedAsync(otherShipmentNumber, _cancellationToken);

        var trail = released["correlationId"]!.GetValue<string>();
        trail.Should().NotBe(planning).And.NotBe(other["correlationId"]!.GetValue<string>());
        var message = await SingleReleasedAsync(released);
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

    [Fact]
    public async Task Release_Always_TracesOnItsOwnTrailWithTheTrackingContractKeys()
    {
        var (number, shipmentNumber) = await PlannedAsync(PlanningCalls.ToCarrier());

        var released = await tms.ReleasedAsync(shipmentNumber, _cancellationToken);

        var trail = released["correlationId"]!.GetValue<string>();
        var log = await tms.TmsLogAsync(
            log => log["correlationId"] == trail && log.Text.Contains("released to", StringComparison.Ordinal),
            _cancellationToken);
        log["flow"].Should().Be("transport-planning");
        log["partyId"].Should().Be("SHP-0001");
        log["transportOrderNumber"].Should().Be(number);
        log["shipmentNumber"].Should().Be(shipmentNumber);
        log["outcome"].Should().Be("Succeeded");
    }

    [Fact]
    public async Task Release_SentAgainUnderItsKey_AnswersAsBefore_AndPublishesNothingMore()
    {
        var (_, shipmentNumber) = await PlannedAsync(PlanningCalls.ToCarrier());
        var key = Guid.NewGuid().ToString();
        using var first = await tms.ReleaseAsync(shipmentNumber, _cancellationToken, key);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var released = JsonNode.Parse(await first.Content.ReadAsStringAsync(_cancellationToken))!;
        await tms.PublishedAsync(released["correlationId"]!.GetValue<string>(), "ShipmentReleased", _cancellationToken);

        using var retry = await tms.ReleaseAsync(shipmentNumber, _cancellationToken, key);

        var body = await retry.Content.ReadAsStringAsync(_cancellationToken);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var repeat = JsonNode.Parse(body)!;
        repeat["correlationId"]!.GetValue<string>().Should().Be(released["correlationId"]!.GetValue<string>());
        repeat["repeat"]!.GetValue<bool>().Should().BeTrue();
        await Task.Delay(TimeSpan.FromSeconds(3), _cancellationToken); // Let the outbox run, so an event would show up.
        tms.ShipmentsReleased(shipmentNumber).Should().ContainSingle("a retry releases nothing, so it publishes nothing");
    }

    [Fact]
    public async Task Release_WithoutAnIdempotencyKey_IsABadRequest()
    {
        var (_, shipmentNumber) = await PlannedAsync(PlanningCalls.ToOwnFleet());
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/shipments/{shipmentNumber}/release", UriKind.Relative));

        using var response = await tms.TmsUi.SendAsync(request, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ShipmentsToRelease_ListsThePlannedShipmentsWithWhoCarriesEach_AndNotTheReleasedOnes()
    {
        var (carrierNumber, carrierShipment) = await PlannedAsync(PlanningCalls.ToCarrier());
        var (_, ownFleetShipment) = await PlannedAsync(PlanningCalls.ToOwnFleet());
        var releasedShipment = await ReleasedAsync();

        var toRelease = await tms.ListShipmentsToReleaseAsync(_cancellationToken);

        var numbers = toRelease.Select(s => s!["shipmentNumber"]!.GetValue<string>()).ToList();
        numbers.Should().Contain([carrierShipment, ownFleetShipment]).And.NotContain(releasedShipment);
        var carrier = toRelease.Single(s => s!["shipmentNumber"]!.GetValue<string>() == carrierShipment)!;
        carrier["transportOrderNumber"]!.GetValue<string>().Should().Be(carrierNumber);
        carrier["shipper"]!["id"]!.GetValue<string>().Should().Be("SHP-0001");
        carrier["carriedBy"]!["assignedTo"]!.GetValue<string>().Should().Be("Carrier");
        carrier["carriedBy"]!["carrier"]!["id"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        var ownFleet = toRelease.Single(s => s!["shipmentNumber"]!.GetValue<string>() == ownFleetShipment)!;
        ownFleet["carriedBy"]!["assignedTo"]!.GetValue<string>().Should().Be("OwnFleet");
        ownFleet["carriedBy"]!["carrier"].Should().BeNull();
    }

    /// <summary>A Transport Order the Planner assigned, whose ShipmentPlanned is out, so later events are the release's.</summary>
    private async Task<(string Number, string ShipmentNumber)> PlannedAsync(JsonObject assignment)
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);
        var assigned = await tms.AssignPlannedAsync(number, assignment, _cancellationToken);
        await tms.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken);
        return (number, assigned["shipmentNumber"]!.GetValue<string>());
    }

    /// <summary>A Carrier Shipment the Planner released, whose ShipmentReleased is out.</summary>
    private async Task<string> ReleasedAsync()
    {
        var (_, shipmentNumber) = await PlannedAsync(PlanningCalls.ToCarrier());
        await SingleReleasedAsync(await tms.ReleasedAsync(shipmentNumber, _cancellationToken));
        return shipmentNumber;
    }

    private async Task<ServiceBusReceivedMessage> SingleReleasedAsync(JsonNode released)
    {
        var events = await tms.PublishedAsync(released["correlationId"]!.GetValue<string>(), "ShipmentReleased", _cancellationToken);
        return events.Should().ContainSingle().Subject;
    }

    private static JsonNode Valid(ServiceBusReceivedMessage message)
    {
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateShipmentReleased(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        return body;
    }
}
