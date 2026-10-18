
namespace Norvia.Tms.Api.IntegrationTests.CarrierBookings;

/// <summary>
/// P4.4's two query changes: the Shipments-to-release query shows each Shipment's last Carrier booking outcome, and a
/// new query lists the Requested Carrier bookings awaiting an answer, sorted by earliest collection time.
/// </summary>
public sealed class CarrierBookingQueriesTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShipmentsToRelease_AShipmentThatCameBackRejected_ShowsTheOutcomeAndReason()
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.RejectedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "NoCapacity", _cancellationToken, "No trucks on Friday.");

        var toRelease = await tms.ListShipmentsToReleaseAsync(_cancellationToken);

        var shipment = toRelease.Single(s => s!["shipmentNumber"]!.GetValue<string>() == shipmentNumber)!;
        var outcome = shipment["lastCarrierBookingOutcome"]!;
        outcome["status"]!.GetValue<string>().Should().Be("Rejected");
        outcome["carrier"]!["id"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        outcome["reasonCode"]!.GetValue<string>().Should().Be("NoCapacity");
        outcome["reasonText"]!.GetValue<string>().Should().Be("No trucks on Friday.");
    }

    [Fact]
    public async Task ShipmentsToRelease_AShipmentThatCameBackWithdrawn_ShowsTheOutcome_WithNoReason()
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken);

        var toRelease = await tms.ListShipmentsToReleaseAsync(_cancellationToken);

        var shipment = toRelease.Single(s => s!["shipmentNumber"]!.GetValue<string>() == shipmentNumber)!;
        var outcome = shipment["lastCarrierBookingOutcome"]!;
        outcome["status"]!.GetValue<string>().Should().Be("Withdrawn");
        outcome["reasonCode"].Should().BeNull();
    }

    [Fact]
    public async Task ShipmentsToRelease_AShipmentNeverReleased_HasNoLastCarrierBookingOutcome()
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);
        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken);
        await tms.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken);

        var toRelease = await tms.ListShipmentsToReleaseAsync(_cancellationToken);

        var shipment = toRelease.Single(s => s!["shipmentNumber"]!.GetValue<string>() == assigned["shipmentNumber"]!.GetValue<string>())!;
        shipment["lastCarrierBookingOutcome"].Should().BeNull();
    }

    [Fact]
    public async Task BookingsAwaitingAnswer_ListsARequestedBooking_NotYetSent()
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        var awaiting = await tms.ListBookingsAwaitingAnswerAsync(_cancellationToken);

        var booking = awaiting.Single(b => b!["carrierBookingNumber"]!.GetValue<string>() == carrierBookingNumber)!;
        booking["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        booking["carrier"]!["id"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        booking["sentAt"].Should().BeNull();
        booking["requestedAt"].Should().NotBeNull();
    }

    [Fact]
    public async Task BookingsAwaitingAnswer_AfterItIsReportedSent_ShowsItsSentTime()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.ReportedSentAsync(carrierBookingNumber, _cancellationToken);

        var awaiting = await tms.ListBookingsAwaitingAnswerAsync(_cancellationToken);

        awaiting.Single(b => b!["carrierBookingNumber"]!.GetValue<string>() == carrierBookingNumber)!["sentAt"].Should().NotBeNull();
    }

    [Fact]
    public async Task BookingsAwaitingAnswer_ListsOnlyRequestedBookings_NotConfirmedRejectedNotDeliveredOrWithdrawnOnes()
    {
        var reference = CarrierBookingCalls.NewReference();
        var (confirmedBooking, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.ConfirmedAsync(confirmedBooking, PlanningCalls.CarrierId, reference, _cancellationToken);
        var (rejectedBooking, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.RejectedAsync(rejectedBooking, PlanningCalls.CarrierId, "NoCapacity", _cancellationToken);
        var (withdrawnBooking, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.WithdrawnAsync(withdrawnBooking, _cancellationToken);
        var (stillOpenBooking, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        var awaiting = await tms.ListBookingsAwaitingAnswerAsync(_cancellationToken);

        var numbers = awaiting.Select(b => b!["carrierBookingNumber"]!.GetValue<string>()).ToList();
        numbers.Should().Contain(stillOpenBooking)
            .And.NotContain([confirmedBooking, rejectedBooking, withdrawnBooking]);
    }
}
