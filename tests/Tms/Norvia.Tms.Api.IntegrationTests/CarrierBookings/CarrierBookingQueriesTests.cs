using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

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
        var (carrierBookingNumber, shipmentNumber) = await ReleasedAsync();
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
        var (carrierBookingNumber, shipmentNumber) = await ReleasedAsync();
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
        var (carrierBookingNumber, shipmentNumber) = await ReleasedAsync();

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
        var (carrierBookingNumber, _) = await ReleasedAsync();
        await tms.ReportedSentAsync(carrierBookingNumber, _cancellationToken);

        var awaiting = await tms.ListBookingsAwaitingAnswerAsync(_cancellationToken);

        awaiting.Single(b => b!["carrierBookingNumber"]!.GetValue<string>() == carrierBookingNumber)!["sentAt"].Should().NotBeNull();
    }

    [Fact]
    public async Task BookingsAwaitingAnswer_ListsOnlyRequestedBookings_NotConfirmedRejectedNotDeliveredOrWithdrawnOnes()
    {
        var (confirmedBooking, _) = await ReleasedAsync();
        await tms.ConfirmedAsync(confirmedBooking, PlanningCalls.CarrierId, "VDT-240917-0042", _cancellationToken);
        var (rejectedBooking, _) = await ReleasedAsync();
        await tms.RejectedAsync(rejectedBooking, PlanningCalls.CarrierId, "NoCapacity", _cancellationToken);
        var (withdrawnBooking, _) = await ReleasedAsync();
        await tms.WithdrawnAsync(withdrawnBooking, _cancellationToken);
        var (stillOpenBooking, _) = await ReleasedAsync();

        var awaiting = await tms.ListBookingsAwaitingAnswerAsync(_cancellationToken);

        var numbers = awaiting.Select(b => b!["carrierBookingNumber"]!.GetValue<string>()).ToList();
        numbers.Should().Contain(stillOpenBooking)
            .And.NotContain([confirmedBooking, rejectedBooking, withdrawnBooking]);
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
