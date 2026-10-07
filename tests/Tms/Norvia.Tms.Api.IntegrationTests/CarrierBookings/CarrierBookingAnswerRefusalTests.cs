using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.CarrierBookings.CarrierBookingCalls;

namespace Norvia.Tms.Api.IntegrationTests.CarrierBookings;

/// <summary>
/// P4's answer rules: the TMS refuses an answer that breaks them, and treats a repeated answer safely. A refusal
/// changes and publishes nothing, and is traced as Refused with its failure code, on the correlation ID the call
/// itself carries.
/// </summary>
public sealed class CarrierBookingAnswerRefusalTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Confirm_AnUnknownBookingNumber_IsRefused_WithBookingNotFound()
    {
        var reference = CarrierBookingCalls.NewReference();
        var problem = await tms.ConfirmRefusedAsync("CB-9999999", PlanningCalls.CarrierId, reference, HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("BookingNotFound");
    }

    [Fact]
    public async Task Reject_AnUnknownBookingNumber_IsRefused_WithBookingNotFound()
    {
        var problem = await tms.RejectRefusedAsync("CB-9999999", PlanningCalls.CarrierId, "NoCapacity", HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("BookingNotFound");
    }

    [Fact]
    public async Task Confirm_FromAnotherCarrier_IsRefused_WithNoStableCode_AndChangesNothing()
    {
        var reference = CarrierBookingCalls.NewReference();
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        var problem = await tms.ConfirmRefusedAsync(
            carrierBookingNumber, PlanningCalls.OtherCarrierId, reference, HttpStatusCode.Forbidden, _cancellationToken);

        problem["code"].Should().BeNull("the design's answer rules give the wrong-Carrier rule no code, only \"Refused\"");
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        BookingOf(shipment, carrierBookingNumber)["status"]!.GetValue<string>().Should().Be("Requested");
    }

    [Fact]
    public async Task Reject_FromAnotherCarrier_IsRefused_WithNoStableCode_AndChangesNothing()
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        var problem = await tms.RejectRefusedAsync(
            carrierBookingNumber, PlanningCalls.OtherCarrierId, "NoCapacity", HttpStatusCode.Forbidden, _cancellationToken);

        problem["code"].Should().BeNull("the design's answer rules give the wrong-Carrier rule no code, only \"Refused\"");
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        BookingOf(shipment, carrierBookingNumber)["status"]!.GetValue<string>().Should().Be("Requested");
    }

    [Fact]
    public async Task Confirm_ABookingAlreadyConfirmedDifferently_IsRefused_WithBookingNotOpen()
    {
        var reference41 = CarrierBookingCalls.NewReference();
        var reference42 = CarrierBookingCalls.NewReference();
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.ConfirmedAsync(carrierBookingNumber, PlanningCalls.CarrierId, reference41, _cancellationToken);

        var problem = await tms.ConfirmRefusedAsync(
            carrierBookingNumber, PlanningCalls.CarrierId, reference42, HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("BookingNotOpen");
    }

    [Fact]
    public async Task Confirm_AReferenceTheCarrierAlreadyHoldsOnAnotherConfirmedBooking_IsRefused_WithReferenceInUse_AndLeavesTheBookingRequested()
    {
        var reference = CarrierBookingCalls.NewReference();
        var (first, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var (second, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.ConfirmedAsync(first, PlanningCalls.CarrierId, reference, _cancellationToken);

        var problem = await tms.ConfirmRefusedAsync(second, PlanningCalls.CarrierId, reference, HttpStatusCode.UnprocessableEntity, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ReferenceInUse");
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        BookingOf(shipment, second)["status"]!.GetValue<string>().Should().Be("Requested");
    }

    [Fact]
    public async Task Reject_ABookingAlreadyRejectedDifferently_IsRefused_WithBookingNotOpen()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.RejectedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "LaneNotServed", _cancellationToken);

        var problem = await tms.RejectRefusedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "NoCapacity", HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("BookingNotOpen");
    }

    [Fact]
    public async Task Confirm_TheExactSameConfirmationRepeated_ReturnsTheOriginalResult_FlaggedAsARepeat_AndPublishesNoNewEvent()
    {
        var reference = CarrierBookingCalls.NewReference();
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var first = await tms.ConfirmedAsync(carrierBookingNumber, PlanningCalls.CarrierId, reference, _cancellationToken);
        var firstCorrelationId = first["correlationId"]!.GetValue<string>();
        await tms.PublishedAsync(firstCorrelationId, "CarrierBookingConfirmed", _cancellationToken);

        var repeated = await tms.ConfirmedAsync(
            carrierBookingNumber, PlanningCalls.CarrierId, reference, _cancellationToken, correlationId: Guid.NewGuid().ToString());

        repeated["repeat"]!.GetValue<bool>().Should().BeTrue();
        repeated["correlationId"]!.GetValue<string>().Should().Be(firstCorrelationId, "a repeat answers on the trail of the answer it repeats");
        (await tms.PublishedAsync(firstCorrelationId, "CarrierBookingConfirmed", _cancellationToken)).Should().ContainSingle();
    }

    [Fact]
    public async Task Reject_TheExactSameRejectionRepeated_ReturnsTheOriginalResult_FlaggedAsARepeat_AndPublishesNoNewEvent()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var first = await tms.RejectedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "NoCapacity", _cancellationToken, "No trucks on Friday.");
        var firstCorrelationId = first["correlationId"]!.GetValue<string>();
        await tms.PublishedAsync(firstCorrelationId, "CarrierBookingRejected", _cancellationToken);

        var repeated = await tms.RejectedAsync(
            carrierBookingNumber,
            PlanningCalls.CarrierId,
            "NoCapacity",
            _cancellationToken,
            "No trucks on Friday.",
            correlationId: Guid.NewGuid().ToString());

        repeated["repeat"]!.GetValue<bool>().Should().BeTrue();
        repeated["correlationId"]!.GetValue<string>().Should().Be(firstCorrelationId, "a repeat answers on the trail of the answer it repeats");
        (await tms.PublishedAsync(firstCorrelationId, "CarrierBookingRejected", _cancellationToken)).Should().ContainSingle();
    }

    [Fact]
    public async Task Reject_WithAnUnknownReasonCode_IsABadRequest()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        using var response = await tms.AnswerAsync(
            carrierBookingNumber,
            new JsonObject { ["carrierId"] = PlanningCalls.CarrierId, ["outcome"] = "Rejected", ["reasonCode"] = "SomeUnknownReason" },
            _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reject_WithOtherAndNoReasonText_IsABadRequest()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        using var response = await tms.AnswerAsync(
            carrierBookingNumber, new JsonObject { ["carrierId"] = PlanningCalls.CarrierId, ["outcome"] = "Rejected", ["reasonCode"] = "Other" }, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reject_WithOtherAndAReasonText_IsAccepted()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        var rejected = await tms.RejectedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "Other", _cancellationToken, "A one-off reason.");

        rejected["status"]!.GetValue<string>().Should().Be("Rejected");
    }

    [Fact]
    public async Task Confirm_Refused_IsTracedAsRefused_WithItsFailureCode()
    {
        var reference = CarrierBookingCalls.NewReference();
        var correlationId = Guid.NewGuid().ToString();

        using var response = await tms.ConfirmAsync(
            "CB-9999999", PlanningCalls.CarrierId, reference, _cancellationToken, correlationId: correlationId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var refusal = await tms.TmsLogAsync(log => log["correlationId"] == correlationId && log["outcome"] == "Refused", _cancellationToken);
        refusal["failureCode"].Should().Be("BookingNotFound");
        refusal["flow"].Should().Be("carrier-booking-answer");
    }

    [Fact]
    public async Task Confirm_RefusedForTheWrongCarrier_IsTracedAsRefused_WithNoFailureCode()
    {
        var reference = CarrierBookingCalls.NewReference();
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var correlationId = Guid.NewGuid().ToString();

        using var response = await tms.ConfirmAsync(
            carrierBookingNumber, PlanningCalls.OtherCarrierId, reference, _cancellationToken, correlationId: correlationId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var refusal = await tms.TmsLogAsync(log => log["correlationId"] == correlationId && log["outcome"] == "Refused", _cancellationToken);
        refusal["failureCode"].Should().BeNull("a key that doesn't apply is left out, never sent empty");
    }
}
