using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.CustomerServiceAgent;

/// <summary>
/// <c>get_transport_order</c>, <c>get_shipment_timeline</c>, <c>get_exceptions</c> and <c>get_proof_of_delivery_status</c> through the stand-in to the TMS API's agent view, for a
/// Transport Order each test carries through its life itself: a delivered one that is Billable, one with an open Exception and one that is not yet billable. No tool answers with
/// a Consignee's details, a remark a reporter typed or a file's name or contents.
/// </summary>
public sealed class TransportOrderToolsTests(CustomerServiceAgentFixture fixture)
{
    private const string TransportOrderTool = "get_transport_order";
    private const string TimelineTool = "get_shipment_timeline";
    private const string ExceptionsTool = "get_exceptions";
    private const string ProofTool = "get_proof_of_delivery_status";

    // What a Carrier's remark, a Proof of delivery's signatory and remark and its file hold in the TMS (the Calls helpers), none of which the Agent may read.
    private static readonly string[] NeverForTheAgent =
    [
        .. CustomerServiceAgentCalls.NeverForTheAgent, "at dock 4", "J. Becker", "Signed at the dock", "cmr.pdf", "%PDF", "Düsseldorf",
    ];

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADeliveredOrder_IsReadAsBillable_WithItsShipmentItsRatingAndItsProofOfDelivery()
    {
        var order = await DeliveredOrderAsync();
        await fixture.ProofRecordedAsync(order.Booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await fixture.WaitForBillableAsync(order.Booked.TransportOrderNumber, _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var summary = await ReadAsync(client, TransportOrderTool, new JsonObject { ["transportOrderNumber"] = order.Booked.TransportOrderNumber });
        var timeline = await ReadAsync(client, TimelineTool, ShipmentArguments(order));
        var exceptions = await ReadAsync(client, ExceptionsTool, ShipmentArguments(order));
        var proof = await ReadAsync(client, ProofTool, ShipmentArguments(order));

        summary["transportOrderNumber"]!.GetValue<string>().Should().Be(order.Booked.TransportOrderNumber);
        summary["shipmentNumber"]!.GetValue<string>().Should().Be(order.Booked.ShipmentNumber);
        summary["shipmentStatus"]!.GetValue<string>().Should().Be("Delivered");
        summary["rating"]!["result"]!.GetValue<string>().Should().Be("Rated");
        summary["rating"]!["failureCode"].Should().BeNull();
        summary["billing"]!["state"]!.GetValue<string>().Should().Be("Billable");
        summary["billing"]!["becameBillableAt"].Should().NotBeNull();
        summary["billing"]!["blockingReasons"]!.AsArray().Should().BeEmpty();
        timeline["trackingEvents"]!.AsArray().Select(trackingEvent => trackingEvent!["type"]!.GetValue<string>()).Should().Equal("Delivered");
        var delivered = timeline["trackingEvents"]![0]!;
        delivered["place"]!.GetValue<string>().Should().Be("Rotterdam");
        delivered["source"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        delivered["movedShipment"]!.GetValue<bool>().Should().BeTrue();
        exceptions["exceptions"]!.AsArray().Should().BeEmpty();
        proof["status"]!.GetValue<string>().Should().Be("Current");
        proof["withReservations"]!.GetValue<bool>().Should().BeFalse();
        var recorded = proof["proofsOfDelivery"]!.AsArray().Should().ContainSingle().Subject!;
        recorded["condition"]!.GetValue<string>().Should().Be("Clean");
        recorded["isCurrent"]!.GetValue<bool>().Should().BeTrue();
        recorded["fileCount"]!.GetValue<int>().Should().Be(1);
        NeverRead(summary, timeline, exceptions, proof);
    }

    [Fact]
    public async Task AnOrderWithAnException_IsReadAsNotYetBillable_WithTheOpenExceptionItsNotesAndTheReservations()
    {
        var order = await DeliveredOrderAsync();
        await fixture.ProofWithReservationsRecordedAsync(order.Booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        var open = (await fixture.OpenExceptionsOfAsync(order.Booked.ShipmentNumber, _cancellationToken)).Should().ContainSingle().Subject;
        var exceptionNumber = open["exceptionNumber"]!.GetValue<string>();
        using (var added = await fixture.AddNoteAsync(exceptionNumber, ExceptionCalls.Note(), _cancellationToken))
        {
            added.IsSuccessStatusCode.Should().BeTrue();
        }

        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var summary = await ReadAsync(client, TransportOrderTool, new JsonObject { ["transportOrderNumber"] = order.Booked.TransportOrderNumber });
        var exceptions = await ReadAsync(client, ExceptionsTool, ShipmentArguments(order));
        var proof = await ReadAsync(client, ProofTool, ShipmentArguments(order));

        summary["billing"]!["state"]!.GetValue<string>().Should().Be("NotYetBillable");
        summary["billing"]!["blockingReasons"]!.AsArray().Select(reason => reason!.GetValue<string>()).Should().Equal("OpenException");
        var exception = exceptions["exceptions"]!.AsArray().Should().ContainSingle().Subject!;
        exception["exceptionNumber"]!.GetValue<string>().Should().Be(exceptionNumber);
        exception["type"]!.GetValue<string>().Should().Be("Reservations");
        exception["status"]!.GetValue<string>().Should().Be("Open");
        exception["causes"]!.AsArray().Should().NotBeEmpty();
        exception["shipperNotified"]!.GetValue<bool>().Should().BeFalse();
        exception["outcome"].Should().BeNull();
        exception["notes"]!.AsArray().Select(note => note!["text"]!.GetValue<string>()).Should().Equal("Phoned Kessler, redelivery Thursday");
        proof["status"]!.GetValue<string>().Should().Be("Current");
        proof["withReservations"]!.GetValue<bool>().Should().BeTrue();
        NeverRead(summary, exceptions, proof);
    }

    [Fact]
    public async Task AnOrderWithAResolvedException_IsReadWithItsOutcome()
    {
        var order = await DeliveredOrderAsync();
        await fixture.ProofWithReservationsRecordedAsync(order.Booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        var open = (await fixture.OpenExceptionsOfAsync(order.Booked.ShipmentNumber, _cancellationToken)).Should().ContainSingle().Subject;
        await fixture.ResolvedAsync(open["exceptionNumber"]!.GetValue<string>(), ExceptionCalls.Resolution("NoActionNeeded"), _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var exceptions = await ReadAsync(client, ExceptionsTool, ShipmentArguments(order));

        var exception = exceptions["exceptions"]!.AsArray().Should().ContainSingle().Subject!;
        exception["status"]!.GetValue<string>().Should().Be("Resolved");
        exception["outcome"]!.GetValue<string>().Should().Be("NoActionNeeded");
        exception["resolvedAt"].Should().NotBeNull();
        NeverRead(exceptions);
    }

    [Fact]
    public async Task ADeliveredOrderWithoutAProofOfDelivery_IsReadAsNotYetBillable_BlockedByTheMissingProof()
    {
        var order = await DeliveredOrderAsync();
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var summary = await ReadAsync(client, TransportOrderTool, new JsonObject { ["transportOrderNumber"] = order.Booked.TransportOrderNumber });
        var proof = await ReadAsync(client, ProofTool, ShipmentArguments(order));
        var exceptions = await ReadAsync(client, ExceptionsTool, ShipmentArguments(order));

        summary["shipmentStatus"]!.GetValue<string>().Should().Be("Delivered");
        summary["billing"]!["state"]!.GetValue<string>().Should().Be("NotYetBillable");
        summary["billing"]!["becameBillableAt"].Should().BeNull();
        summary["billing"]!["blockingReasons"]!.AsArray().Select(reason => reason!.GetValue<string>()).Should().Equal("NoProofOfDelivery");
        proof["status"]!.GetValue<string>().Should().BeOneOf(["NotDueYet", "Missing"], "the TMS's setting says how long a Proof of delivery may take");
        proof["withReservations"].Should().BeNull();
        proof["proofsOfDelivery"]!.AsArray().Should().BeEmpty();
        exceptions["exceptions"]!.AsArray().Should().BeEmpty();
        NeverRead(summary, proof, exceptions);
    }

    [Fact]
    public async Task ABookedShipment_IsReadAsNotDelivered_WithNoTrackingEventsAndNotBillable()
    {
        var number = await fixture.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);
        await fixture.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        var booked = await fixture.BookedAsync(number, _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var summary = await ReadAsync(client, TransportOrderTool, new JsonObject { ["transportOrderNumber"] = number });
        var timeline = await ReadAsync(client, TimelineTool, new JsonObject { ["shipmentNumber"] = booked.ShipmentNumber });
        var proof = await ReadAsync(client, ProofTool, new JsonObject { ["shipmentNumber"] = booked.ShipmentNumber });

        summary["shipmentStatus"]!.GetValue<string>().Should().Be("Booked");
        summary["billing"]!["state"]!.GetValue<string>().Should().Be("NotYetBillable");
        summary["billing"]!["becameBillableAt"].Should().BeNull();
        timeline["trackingEvents"]!.AsArray().Should().BeEmpty();
        proof["status"]!.GetValue<string>().Should().Be("NotDelivered");
    }

    [Fact]
    public async Task ASecondTrackingEvent_IsReadAfterTheFirst_InEventTimeOrder_WithItsSource()
    {
        var booked = await fixture.BookedShipmentAsync(_cancellationToken);
        await fixture.RecordedAsync(TrackingEventCalls.Event("Delivered", booked.CarrierBookingReference, DateTimeOffset.UtcNow.AddMinutes(-1)), _cancellationToken);
        await fixture.RecordedAsync(TrackingEventCalls.Event("Collected", booked.CarrierBookingReference, DateTimeOffset.UtcNow.AddMinutes(-30)), _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var timeline = await ReadAsync(client, TimelineTool, new JsonObject { ["shipmentNumber"] = booked.ShipmentNumber });

        var events = timeline["trackingEvents"]!.AsArray();
        events.Select(trackingEvent => trackingEvent!["type"]!.GetValue<string>()).Should().Equal("Collected", "Delivered");
        events.Select(trackingEvent => trackingEvent!["source"]!.GetValue<string>()).Should().AllBe(PlanningCalls.CarrierId);
        NeverRead(timeline);
    }

    [Theory]
    [InlineData(TransportOrderTool, "transportOrderNumber", "TO-NOSUCHORDER")]
    [InlineData(TimelineTool, "shipmentNumber", "SH-NOSUCHSHIPMENT")]
    [InlineData(ExceptionsTool, "shipmentNumber", "SH-NOSUCHSHIPMENT")]
    [InlineData(ProofTool, "shipmentNumber", "SH-NOSUCHSHIPMENT")]
    public async Task AToolAskedForSomethingThatDoesNotExist_ReturnsAnError(string tool, string argument, string number)
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (isError, _) = await client.CallToolAsync(tool, new JsonObject { [argument] = number }, Guid.NewGuid().ToString(), _cancellationToken);

        isError.Should().BeTrue();
    }

    [Fact]
    public async Task TheTools_AreListedByTheStandIn()
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        using var response = await client.CallAsync("tools/list", _cancellationToken);

        var listed = await response.Content.ReadAsStringAsync(_cancellationToken);
        listed.Should().ContainAll(CustomerServiceAgentCalls.Tool, TransportOrderTool, TimelineTool, ExceptionsTool, ProofTool);
    }

    private sealed record Delivered(BillingCalls.BookedOrder Booked);

    private static JsonObject ShipmentArguments(Delivered order) => new() { ["shipmentNumber"] = order.Booked.ShipmentNumber };

    /// <summary>A Transport Order that is rated, Booked with the Carrier and reported Delivered; what else happens to it is the test's own.</summary>
    private async Task<Delivered> DeliveredOrderAsync()
    {
        var number = await fixture.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);
        await fixture.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        var booked = await fixture.BookedAsync(number, _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();
        await fixture.DeliveredAsync(booked, correlationId, _cancellationToken);
        await fixture.PublishedAsync(correlationId, "ShipmentDelivered", _cancellationToken);
        return new Delivered(booked);
    }

    private async Task<JsonNode> ReadAsync(HttpClient client, string tool, JsonObject arguments)
    {
        var (isError, text) = await client.CallToolAsync(tool, arguments, Guid.NewGuid().ToString(), _cancellationToken);
        isError.Should().BeFalse(text);
        return JsonNode.Parse(text)!;
    }

    private static void NeverRead(params JsonNode[] answers)
    {
        foreach (var answer in answers)
        {
            answer.ToJsonString().Should().NotContainAny(NeverForTheAgent);
        }
    }
}
