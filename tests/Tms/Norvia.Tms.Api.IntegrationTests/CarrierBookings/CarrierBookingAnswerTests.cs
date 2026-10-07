using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.CarrierBookings.CarrierBookingCalls;

namespace Norvia.Tms.Api.IntegrationTests.CarrierBookings;

/// <summary>
/// A Carrier confirms or rejects a Requested Carrier booking (P4), on a Message trail of its own. This file covers the
/// happy path: a well-formed confirmation or rejection for a Requested booking that exists. BookingNotFound, the
/// wrong-Carrier check, BookingNotOpen, idempotent repeats and role enforcement are in
/// <see cref="CarrierBookingAnswerRefusalTests"/> and <see cref="CarrierBookingAnswerAuthorizationTests"/>.
/// </summary>
public sealed class CarrierBookingAnswerTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Confirm_ARequestedBooking_MovesItToConfirmed_AndTheShipmentToBooked_AndPublishesOneCarrierBookingConfirmed()
    {
        var reference = CarrierBookingCalls.NewReference();
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var plannedCollectionTime = DateTimeOffset.UtcNow.AddDays(1);

        var confirmed = await tms.ConfirmedAsync(carrierBookingNumber, PlanningCalls.CarrierId, reference, _cancellationToken, plannedCollectionTime);

        confirmed["carrierBookingNumber"]!.GetValue<string>().Should().Be(carrierBookingNumber);
        confirmed["status"]!.GetValue<string>().Should().Be("Confirmed");
        confirmed["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        confirmed["shipmentStatus"]!.GetValue<string>().Should().Be("Booked");

        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Booked");
        var booking = BookingOf(shipment, carrierBookingNumber);
        booking["status"]!.GetValue<string>().Should().Be("Confirmed");
        booking["carrierBookingReference"]!.GetValue<string>().Should().Be(reference);
        DateTimeOffset.Parse(booking["plannedCollectionTime"]!.GetValue<string>(), CultureInfo.InvariantCulture).Should().BeCloseTo(plannedCollectionTime, TimeSpan.FromSeconds(1));
        booking["answeredAt"].Should().NotBeNull();

        var message = await SingleConfirmedAsync(confirmed);
        var body = Valid(message, CanonicalModelSchemas.ValidateCarrierBookingConfirmed);
        body["carrierBookingNumber"]!.GetValue<string>().Should().Be(carrierBookingNumber);
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["carrierId"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        body["carrierBookingReference"]!.GetValue<string>().Should().Be(reference);
        message.ApplicationProperties.Should().Contain("carrierBookingNumber", carrierBookingNumber);
    }

    [Fact]
    public async Task Confirm_WithNoPlannedCollectionTime_StoresNone_AndTheEventCarriesNone()
    {
        var reference = CarrierBookingCalls.NewReference();
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        var confirmed = await tms.ConfirmedAsync(carrierBookingNumber, PlanningCalls.CarrierId, reference, _cancellationToken);

        var message = await SingleConfirmedAsync(confirmed);
        var body = Valid(message, CanonicalModelSchemas.ValidateCarrierBookingConfirmed);
        body["plannedCollectionTime"].Should().BeNull();
    }

    [Fact]
    public async Task Confirm_TheAnswersOwnCorrelationId_IsWhatTheEventCarries()
    {
        var reference = CarrierBookingCalls.NewReference();
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var answerCorrelationId = Guid.NewGuid().ToString();

        var confirmed = await tms.ConfirmedAsync(
            carrierBookingNumber, PlanningCalls.CarrierId, reference, _cancellationToken, correlationId: answerCorrelationId);

        confirmed["correlationId"]!.GetValue<string>().Should().Be(answerCorrelationId);
        var message = await SingleConfirmedAsync(confirmed);
        message.CorrelationId.Should().Be(answerCorrelationId);
        JsonNode.Parse(message.Body.ToString())!["correlationId"]!.GetValue<string>().Should().Be(answerCorrelationId);
        message.ApplicationProperties.Should().Contain("correlationId", answerCorrelationId);
    }

    [Fact]
    public async Task Reject_ARequestedBooking_MovesItToRejected_AndTheShipmentBackToPlanned_AndPublishesOneCarrierBookingRejected()
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

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
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
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
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var before = tms.Published.Count(m => NamesShipment(m, shipmentNumber));

        var rejected = await tms.RejectedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "NoCapacity", _cancellationToken);
        await SingleRejectedAsync(rejected); // Lets the outbox settle, so an event of the Shipment's own would show up too.

        var after = tms.Published.Count(m => NamesShipment(m, shipmentNumber));
        (after - before).Should().Be(1, "only CarrierBookingRejected names the Shipment; its move back to Planned publishes nothing of its own");
    }

    [Fact]
    public async Task Confirm_WithNoCarrierBookingReference_IsABadRequest()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        using var response = await AnswerAsync(carrierBookingNumber, new JsonObject { ["carrierId"] = PlanningCalls.CarrierId, ["outcome"] = "Confirmed" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reject_WithNoReasonCode_IsABadRequest()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

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
}
