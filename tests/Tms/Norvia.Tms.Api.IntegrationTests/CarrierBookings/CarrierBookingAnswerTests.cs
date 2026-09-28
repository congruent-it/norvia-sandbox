using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.CarrierBookings;

/// <summary>
/// A Carrier confirms or rejects a Requested Carrier booking (P4), on a Message trail of its own. This slice covers
/// only the happy path: a well-formed confirmation or rejection for a Requested booking that exists. BookingNotFound,
/// the wrong-Carrier check, BookingNotOpen, field validation, idempotent repeats and role enforcement are later tickets.
/// </summary>
public sealed class CarrierBookingAnswerTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Confirm_ARequestedBooking_MovesItToConfirmed_AndTheShipmentToBooked_AndPublishesOneCarrierBookingConfirmed()
    {
        var (carrierBookingNumber, shipmentNumber) = await ReleasedAsync();
        var plannedCollectionTime = DateTimeOffset.UtcNow.AddDays(1);

        var confirmed = await tms.ConfirmedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "VDT-240917-0042", _cancellationToken, plannedCollectionTime);

        confirmed["carrierBookingNumber"]!.GetValue<string>().Should().Be(carrierBookingNumber);
        confirmed["status"]!.GetValue<string>().Should().Be("Confirmed");
        confirmed["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        confirmed["shipmentStatus"]!.GetValue<string>().Should().Be("Booked");

        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Booked");
        var booking = BookingOf(shipment, carrierBookingNumber);
        booking["status"]!.GetValue<string>().Should().Be("Confirmed");
        booking["carrierBookingReference"]!.GetValue<string>().Should().Be("VDT-240917-0042");
        DateTimeOffset.Parse(booking["plannedCollectionTime"]!.GetValue<string>(), CultureInfo.InvariantCulture).Should().BeCloseTo(plannedCollectionTime, TimeSpan.FromSeconds(1));
        booking["answeredAt"].Should().NotBeNull();

        var message = await SingleConfirmedAsync(confirmed);
        var body = Valid(message, CanonicalModelSchemas.ValidateCarrierBookingConfirmed);
        body["carrierBookingNumber"]!.GetValue<string>().Should().Be(carrierBookingNumber);
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["carrierId"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        body["carrierBookingReference"]!.GetValue<string>().Should().Be("VDT-240917-0042");
        message.ApplicationProperties.Should().Contain("carrierBookingNumber", carrierBookingNumber);
    }

    [Fact]
    public async Task Confirm_WithNoPlannedCollectionTime_StoresNone_AndTheEventCarriesNone()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();

        var confirmed = await tms.ConfirmedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "VDT-240917-0043", _cancellationToken);

        var message = await SingleConfirmedAsync(confirmed);
        var body = Valid(message, CanonicalModelSchemas.ValidateCarrierBookingConfirmed);
        body["plannedCollectionTime"].Should().BeNull();
    }

    [Fact]
    public async Task Confirm_TheAnswersOwnCorrelationId_IsWhatTheEventCarries()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();
        var answerCorrelationId = Guid.NewGuid().ToString();

        var confirmed = await tms.ConfirmedAsync(
            carrierBookingNumber, PlanningCalls.CarrierId, "VDT-240917-0042", _cancellationToken, correlationId: answerCorrelationId);

        confirmed["correlationId"]!.GetValue<string>().Should().Be(answerCorrelationId);
        var message = await SingleConfirmedAsync(confirmed);
        message.CorrelationId.Should().Be(answerCorrelationId);
        JsonNode.Parse(message.Body.ToString())!["correlationId"]!.GetValue<string>().Should().Be(answerCorrelationId);
        message.ApplicationProperties.Should().Contain("correlationId", answerCorrelationId);
    }

    [Fact]
    public async Task Reject_ARequestedBooking_MovesItToRejected_AndTheShipmentBackToPlanned_AndPublishesOneCarrierBookingRejected()
    {
        var (carrierBookingNumber, shipmentNumber) = await ReleasedAsync();

        var rejected = await tms.RejectedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "NoCapacity", _cancellationToken, "No trucks on Friday.");

        rejected["carrierBookingNumber"]!.GetValue<string>().Should().Be(carrierBookingNumber);
        rejected["status"]!.GetValue<string>().Should().Be("Rejected");
        rejected["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        rejected["shipmentStatus"]!.GetValue<string>().Should().Be("Planned");

        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Planned");
        var booking = BookingOf(shipment, carrierBookingNumber);
        booking["status"]!.GetValue<string>().Should().Be("Rejected");
        booking["reasonCode"]!.GetValue<string>().Should().Be("NoCapacity");
        booking["reasonText"]!.GetValue<string>().Should().Be("No trucks on Friday.");
        booking["answeredAt"].Should().NotBeNull();

        var message = await SingleRejectedAsync(rejected);
        var body = Valid(message, CanonicalModelSchemas.ValidateCarrierBookingRejected);
        body["carrierBookingNumber"]!.GetValue<string>().Should().Be(carrierBookingNumber);
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["carrierId"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        body["reasonCode"]!.GetValue<string>().Should().Be("NoCapacity");
        body["reasonText"]!.GetValue<string>().Should().Be("No trucks on Friday.");
        message.ApplicationProperties.Should().Contain("carrierBookingNumber", carrierBookingNumber);
    }

    [Fact]
    public async Task Reject_TheAnswersOwnCorrelationId_IsWhatTheEventCarries()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();
        var answerCorrelationId = Guid.NewGuid().ToString();

        var rejected = await tms.RejectedAsync(
            carrierBookingNumber, PlanningCalls.CarrierId, "NoCapacity", _cancellationToken, correlationId: answerCorrelationId);

        rejected["correlationId"]!.GetValue<string>().Should().Be(answerCorrelationId);
        var message = await SingleRejectedAsync(rejected);
        message.CorrelationId.Should().Be(answerCorrelationId);
    }

    [Fact]
    public async Task Reject_TheShipmentsMoveBackToPlanned_PublishesNoEventOfItsOwn()
    {
        var (carrierBookingNumber, shipmentNumber) = await ReleasedAsync();
        var before = tms.Published.Count(m => Names(m, shipmentNumber));

        var rejected = await tms.RejectedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "NoCapacity", _cancellationToken);
        await SingleRejectedAsync(rejected); // Lets the outbox settle, so an event of the Shipment's own would show up too.

        var after = tms.Published.Count(m => Names(m, shipmentNumber));
        (after - before).Should().Be(1, "only CarrierBookingRejected names the Shipment; its move back to Planned publishes nothing of its own");
    }

    [Fact]
    public async Task Confirm_WithNoCarrierBookingReference_IsABadRequest()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();

        using var response = await AnswerAsync(carrierBookingNumber, new JsonObject { ["carrierId"] = PlanningCalls.CarrierId, ["outcome"] = "Confirmed" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reject_WithNoReasonCode_IsABadRequest()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();

        using var response = await AnswerAsync(carrierBookingNumber, new JsonObject { ["carrierId"] = PlanningCalls.CarrierId, ["outcome"] = "Rejected" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<HttpResponseMessage> AnswerAsync(string carrierBookingNumber, JsonObject answer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/answer", UriKind.Relative))
        {
            Content = JsonContent.Create(answer),
        };
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        return await tms.CarrierAnswer.SendAsync(request, _cancellationToken);
    }

    private static bool Names(ServiceBusReceivedMessage message, string shipmentNumber) =>
        message.ApplicationProperties.TryGetValue("shipmentNumber", out var number) && Equals(number, shipmentNumber);

    private static JsonNode BookingOf(JsonNode shipment, string carrierBookingNumber) =>
        shipment["carrierBookings"]!.AsArray().Single(booking => booking!["carrierBookingNumber"]!.GetValue<string>() == carrierBookingNumber)!;

    private async Task<ServiceBusReceivedMessage> SingleConfirmedAsync(JsonNode confirmed)
    {
        var events = await tms.PublishedAsync(confirmed["correlationId"]!.GetValue<string>(), "CarrierBookingConfirmed", _cancellationToken);
        return events.Should().ContainSingle().Subject;
    }

    private async Task<ServiceBusReceivedMessage> SingleRejectedAsync(JsonNode rejected)
    {
        var events = await tms.PublishedAsync(rejected["correlationId"]!.GetValue<string>(), "CarrierBookingRejected", _cancellationToken);
        return events.Should().ContainSingle().Subject;
    }

    private static JsonNode Valid(ServiceBusReceivedMessage message, Func<JsonNode, Json.Schema.EvaluationResults> validate)
    {
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = validate(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        return body;
    }

    /// <summary>A Carrier Shipment the Planner released, with its Requested Carrier booking.</summary>
    private async Task<(string CarrierBookingNumber, string ShipmentNumber)> ReleasedAsync()
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);
        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken);
        await tms.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken);
        var shipmentNumber = assigned["shipmentNumber"]!.GetValue<string>();

        var released = await tms.ReleasedAsync(shipmentNumber, _cancellationToken);
        await tms.PublishedAsync(released["correlationId"]!.GetValue<string>(), "ShipmentReleased", _cancellationToken);
        return (released["carrierBookingNumber"]!.GetValue<string>(), shipmentNumber);
    }
}
