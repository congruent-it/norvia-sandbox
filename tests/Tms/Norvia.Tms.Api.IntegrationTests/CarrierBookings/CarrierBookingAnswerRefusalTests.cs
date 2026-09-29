using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

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
        var problem = await tms.ConfirmRefusedAsync("CB-9999999", PlanningCalls.CarrierId, "VDT-240917-0042", HttpStatusCode.NotFound, _cancellationToken);

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
        var (carrierBookingNumber, shipmentNumber) = await ReleasedAsync();

        var problem = await tms.ConfirmRefusedAsync(
            carrierBookingNumber, PlanningCalls.OtherCarrierId, "VDT-240917-0042", HttpStatusCode.Forbidden, _cancellationToken);

        problem["code"].Should().BeNull("the design's answer rules give the wrong-Carrier rule no code, only \"Refused\"");
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        BookingOf(shipment, carrierBookingNumber)["status"]!.GetValue<string>().Should().Be("Requested");
    }

    [Fact]
    public async Task Reject_FromAnotherCarrier_IsRefused_WithNoStableCode_AndChangesNothing()
    {
        var (carrierBookingNumber, shipmentNumber) = await ReleasedAsync();

        var problem = await tms.RejectRefusedAsync(
            carrierBookingNumber, PlanningCalls.OtherCarrierId, "NoCapacity", HttpStatusCode.Forbidden, _cancellationToken);

        problem["code"].Should().BeNull("the design's answer rules give the wrong-Carrier rule no code, only \"Refused\"");
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        BookingOf(shipment, carrierBookingNumber)["status"]!.GetValue<string>().Should().Be("Requested");
    }

    [Fact]
    public async Task Confirm_ABookingAlreadyConfirmedDifferently_IsRefused_WithBookingNotOpen()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();
        await tms.ConfirmedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "VDT-240917-0041", _cancellationToken);

        var problem = await tms.ConfirmRefusedAsync(
            carrierBookingNumber, PlanningCalls.CarrierId, "VDT-240917-0042", HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("BookingNotOpen");
    }

    [Fact]
    public async Task Reject_ABookingAlreadyRejectedDifferently_IsRefused_WithBookingNotOpen()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();
        await tms.RejectedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "LaneNotServed", _cancellationToken);

        var problem = await tms.RejectRefusedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "NoCapacity", HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("BookingNotOpen");
    }

    [Fact]
    public async Task Confirm_TheExactSameConfirmationRepeated_ReturnsTheOriginalResult_FlaggedAsARepeat_AndPublishesNoNewEvent()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();
        var first = await tms.ConfirmedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "VDT-240917-0042", _cancellationToken);
        var firstCorrelationId = first["correlationId"]!.GetValue<string>();
        await tms.PublishedAsync(firstCorrelationId, "CarrierBookingConfirmed", _cancellationToken);

        var repeated = await tms.ConfirmedAsync(
            carrierBookingNumber, PlanningCalls.CarrierId, "VDT-240917-0042", _cancellationToken, correlationId: Guid.NewGuid().ToString());

        repeated["repeat"]!.GetValue<bool>().Should().BeTrue();
        repeated["correlationId"]!.GetValue<string>().Should().Be(firstCorrelationId, "a repeat answers on the trail of the answer it repeats");
        (await tms.PublishedAsync(firstCorrelationId, "CarrierBookingConfirmed", _cancellationToken)).Should().ContainSingle();
    }

    [Fact]
    public async Task Reject_TheExactSameRejectionRepeated_ReturnsTheOriginalResult_FlaggedAsARepeat_AndPublishesNoNewEvent()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();
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
        var (carrierBookingNumber, _) = await ReleasedAsync();

        using var response = await tms.AnswerAsync(
            carrierBookingNumber,
            new JsonObject { ["carrierId"] = PlanningCalls.CarrierId, ["outcome"] = "Rejected", ["reasonCode"] = "SomeUnknownReason" },
            _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reject_WithOtherAndNoReasonText_IsABadRequest()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();

        using var response = await tms.AnswerAsync(
            carrierBookingNumber, new JsonObject { ["carrierId"] = PlanningCalls.CarrierId, ["outcome"] = "Rejected", ["reasonCode"] = "Other" }, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reject_WithOtherAndAReasonText_IsAccepted()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();

        var rejected = await tms.RejectedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "Other", _cancellationToken, "A one-off reason.");

        rejected["status"]!.GetValue<string>().Should().Be("Rejected");
    }

    [Fact]
    public async Task Confirm_Refused_IsTracedAsRefused_WithItsFailureCode()
    {
        var correlationId = Guid.NewGuid().ToString();

        using var response = await tms.ConfirmAsync(
            "CB-9999999", PlanningCalls.CarrierId, "VDT-240917-0042", _cancellationToken, correlationId: correlationId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var refusal = await tms.TmsLogAsync(log => log["correlationId"] == correlationId && log["outcome"] == "Refused", _cancellationToken);
        refusal["failureCode"].Should().Be("BookingNotFound");
        refusal["flow"].Should().Be("carrier-booking-answer");
    }

    [Fact]
    public async Task Confirm_RefusedForTheWrongCarrier_IsTracedAsRefused_WithNoFailureCode()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();
        var correlationId = Guid.NewGuid().ToString();

        using var response = await tms.ConfirmAsync(
            carrierBookingNumber, PlanningCalls.OtherCarrierId, "VDT-240917-0042", _cancellationToken, correlationId: correlationId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var refusal = await tms.TmsLogAsync(log => log["correlationId"] == correlationId && log["outcome"] == "Refused", _cancellationToken);
        refusal["failureCode"].Should().BeNull("a key that doesn't apply is left out, never sent empty");
    }

    private static JsonNode BookingOf(JsonNode shipment, string carrierBookingNumber) =>
        shipment["carrierBookings"]!.AsArray().Single(booking => booking!["carrierBookingNumber"]!.GetValue<string>() == carrierBookingNumber)!;

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
