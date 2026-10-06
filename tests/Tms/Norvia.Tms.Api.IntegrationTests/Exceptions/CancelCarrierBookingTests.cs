using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.CarrierBookings;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.ProofsOfDelivery;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.Exceptions.ExceptionCalls;
using static Norvia.Tms.Api.IntegrationTests.ProofsOfDelivery.ProofOfDeliveryCalls;

namespace Norvia.Tms.Api.IntegrationTests.Exceptions;

/// <summary>
/// From an open Exception the Planner cancels a Confirmed Carrier booking while the Shipment is Booked (P7, ADR 0010): in one
/// transaction the booking becomes Cancelled, the Shipment goes back to Planned and CarrierBookingCancelled is published. The
/// Shipment can be released again, and the old Carrier's later Tracking events and Proofs of delivery are refused.
/// </summary>
public sealed class CancelCarrierBookingTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cancel_ABookedShipmentsConfirmedBooking_PublishesCarrierBookingCancelled_OnANewCorrelationId_AndKeepsTheExceptionOpen()
    {
        var (shipmentNumber, carrierBookingNumber, reference, exceptionNumber) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);

        using var response = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(), _cancellationToken);

        var answer = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, answer);
        var correlationId = JsonNode.Parse(answer)!["correlationId"]!.GetValue<string>();
        var message = (await tms.PublishedAsync(correlationId, "CarrierBookingCancelled", _cancellationToken)).Should().ContainSingle().Subject;
        var body = BodyOf(message);
        var results = CanonicalModelSchemas.ValidateCarrierBookingCancelled(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["carrierBookingNumber"]!.GetValue<string>().Should().Be(carrierBookingNumber);
        body["carrierBookingReference"]!.GetValue<string>().Should().Be(reference);
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["exceptionNumber"]!.GetValue<string>().Should().Be(exceptionNumber);
        body["reasonText"]!.GetValue<string>().Should().Be("Carrier won't make its collection");
        (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().ContainSingle("cancelling doesn't resolve the Exception");
    }

    [Fact]
    public async Task Cancel_ReturnsTheShipmentToShipmentsToRelease_AsCancelledByNorviaWithTheReason()
    {
        var (shipmentNumber, _, _, exceptionNumber) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);

        using var response = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var toRelease = await tms.ListShipmentsToReleaseAsync(_cancellationToken);
        var outcome = toRelease.Single(s => s!["shipmentNumber"]!.GetValue<string>() == shipmentNumber)!["lastCarrierBookingOutcome"]!;
        outcome["status"]!.GetValue<string>().Should().Be("Cancelled");
        outcome["reasonText"]!.GetValue<string>().Should().Be("Carrier won't make its collection");
    }

    [Fact]
    public async Task Cancel_ThenReleasingAgain_CreatesANewCarrierBooking()
    {
        var (shipmentNumber, carrierBookingNumber, _, exceptionNumber) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);
        using var cancelled = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(), _cancellationToken);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);

        var released = await tms.ReleasedAsync(shipmentNumber, _cancellationToken);

        released["carrierBookingNumber"]!.GetValue<string>().Should().NotBe(carrierBookingNumber);
    }

    [Fact]
    public async Task Cancel_TheSameCancellationSentAgain_IsAnsweredAsBefore_AndPublishesOnce()
    {
        var (_, _, _, exceptionNumber) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);
        var key = Guid.NewGuid().ToString();
        using var first = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(), _cancellationToken, key);
        var firstCorrelationId = JsonNode.Parse(await first.Content.ReadAsStringAsync(_cancellationToken))!["correlationId"]!.GetValue<string>();
        await tms.PublishedAsync(firstCorrelationId, "CarrierBookingCancelled", _cancellationToken);

        using var retry = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(), _cancellationToken, key);

        var answer = await retry.Content.ReadAsStringAsync(_cancellationToken);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, answer);
        var repeated = JsonNode.Parse(answer)!;
        repeated["correlationId"]!.GetValue<string>().Should().Be(firstCorrelationId);
        repeated["repeat"]!.GetValue<bool>().Should().BeTrue();
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Count(m => m.CorrelationId == firstCorrelationId && m.Subject == "CarrierBookingCancelled").Should().Be(1);
    }

    [Fact]
    public async Task Cancel_ACollectedShipment_IsRefused_WithShipmentAlreadyCollected()
    {
        var (_, _, reference, exceptionNumber) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);
        await tms.RecordedAsync(TrackingEventCalls.Event("Collected", reference, DateTimeOffset.UtcNow.AddMinutes(-2)), _cancellationToken);

        using var response = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(), _cancellationToken);

        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        JsonNode.Parse(body)!["code"]!.GetValue<string>().Should().Be("ShipmentAlreadyCollected");
    }

    [Fact]
    public async Task Cancel_ABookingAlreadyCancelled_IsRefused_WithNoConfirmedCarrierBooking()
    {
        var (_, _, _, exceptionNumber) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);
        using var first = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(), _cancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(), _cancellationToken);

        var body = await second.Content.ReadAsStringAsync(_cancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        JsonNode.Parse(body)!["code"]!.GetValue<string>().Should().Be("NoConfirmedCarrierBooking");
    }

    [Fact]
    public async Task Cancel_WithoutAReason_IsRefused_AsAClientError()
    {
        var (_, _, _, exceptionNumber) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);

        using var missing = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(reason: null), _cancellationToken);
        using var blank = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(reason: "  "), _cancellationToken);

        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        blank.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancel_AnUnknownException_IsRefused_WithExceptionNotFound()
    {
        using var response = await tms.CancelCarrierBookingAsync("EX-9999999", Cancellation(), _cancellationToken);

        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        JsonNode.Parse(body)!["code"]!.GetValue<string>().Should().Be("ExceptionNotFound");
    }

    [Fact]
    public async Task AfterTheCancellation_TheOldCarriersTrackingEvent_WithAnEarlierEventTime_IsRefused()
    {
        var (_, _, reference, exceptionNumber) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);
        using var cancelled = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(), _cancellationToken);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);

        var problem = await tms.RecordRefusedAsync(
            TrackingEventCalls.Event("Collected", reference, DateTimeOffset.UtcNow.AddHours(-3)), HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotFound");
    }

    [Fact]
    public async Task AfterTheCancellation_TheOldCarriersProofOfDelivery_IsRefused()
    {
        var (_, _, reference, exceptionNumber) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);
        using var cancelled = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(), _cancellationToken);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);

        var problem = await tms.RecordProofRefusedAsync(Metadata(reference), [Cmr()], HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotFound");
    }
}
