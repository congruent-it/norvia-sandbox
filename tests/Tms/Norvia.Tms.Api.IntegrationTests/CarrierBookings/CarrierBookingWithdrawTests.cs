using System.Net;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.CarrierBookings;

/// <summary>
/// The Planner withdraws a Requested Carrier booking, from the TMS UI (P4.4). A later answer for a withdrawn booking is
/// refused with BookingNotOpen (P4.3), and a later sent or not-delivered report for it is ignored (P4.4).
/// </summary>
public sealed class CarrierBookingWithdrawTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Withdraw_ARequestedBooking_MovesItToWithdrawn_AndTheShipmentToPlanned_AndPublishesOneCarrierBookingWithdrawn()
    {
        var (carrierBookingNumber, shipmentNumber) = await ReleasedAsync();

        var withdrawn = await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken);

        withdrawn["status"]!.GetValue<string>().Should().Be("Withdrawn");
        withdrawn["shipmentStatus"]!.GetValue<string>().Should().Be("Planned");
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Planned");
        BookingOf(shipment, carrierBookingNumber)["status"]!.GetValue<string>().Should().Be("Withdrawn");

        var events = await tms.PublishedAsync(withdrawn["correlationId"]!.GetValue<string>(), "CarrierBookingWithdrawn", _cancellationToken);
        var message = events.Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        var validation = CanonicalModelSchemas.ValidateCarrierBookingWithdrawn(body);
        validation.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(validation));
        body["carrierBookingNumber"]!.GetValue<string>().Should().Be(carrierBookingNumber);
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
    }

    [Fact]
    public async Task Withdraw_TheSameWithdrawalSentAgain_AnswersAsBeforeWithoutWithdrawingAgain()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();
        var key = Guid.NewGuid().ToString();
        var first = await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken, key);
        var before = tms.Published.Count(m => Names(m, carrierBookingNumber) && m.Subject == "CarrierBookingWithdrawn");

        var repeated = await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken, key);

        repeated["correlationId"]!.GetValue<string>().Should().Be(first["correlationId"]!.GetValue<string>());
        tms.Published.Count(m => Names(m, carrierBookingNumber) && m.Subject == "CarrierBookingWithdrawn").Should().Be(before, "a repeat publishes no new event");
    }

    [Fact]
    public async Task Withdraw_AnUnknownBookingNumber_IsRefused_WithBookingNotFound()
    {
        var problem = await tms.WithdrawRefusedAsync("CB-9999999", HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("BookingNotFound");
    }

    [Fact]
    public async Task Withdraw_ABookingAlreadyWithdrawnUnderADifferentKey_IsRefused_WithBookingNotOpen()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();
        await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken);

        var problem = await tms.WithdrawRefusedAsync(carrierBookingNumber, HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("BookingNotOpen");
    }

    [Fact]
    public async Task Withdraw_ABookingAlreadyConfirmed_IsRefused_WithBookingNotOpen()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();
        await tms.ConfirmedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "VDT-240917-0042", _cancellationToken);

        var problem = await tms.WithdrawRefusedAsync(carrierBookingNumber, HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("BookingNotOpen");
    }

    [Fact]
    public async Task Confirm_AWithdrawnBooking_IsRefused_WithBookingNotOpen()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();
        await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken);

        var problem = await tms.ConfirmRefusedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "VDT-240917-0042", HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("BookingNotOpen");
    }

    [Fact]
    public async Task ReportSent_AWithdrawnBooking_IsIgnored_AndChangesNothing()
    {
        var (carrierBookingNumber, _) = await ReleasedAsync();
        await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken);

        var reported = await tms.ReportedSentAsync(carrierBookingNumber, _cancellationToken);

        reported["ignored"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Withdraw_AfterABookingEndsUnconfirmed_ReleasingTheShipmentAgain_CreatesASecondCarrierBookingWithTheSameShipmentNumber()
    {
        var (firstBookingNumber, shipmentNumber) = await ReleasedAsync();
        await tms.WithdrawnAsync(firstBookingNumber, _cancellationToken);

        var released = await tms.ReleasedAsync(shipmentNumber, _cancellationToken);

        var secondBookingNumber = released["carrierBookingNumber"]!.GetValue<string>();
        secondBookingNumber.Should().NotBe(firstBookingNumber);
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Released");
        BookingOf(shipment, firstBookingNumber)["status"]!.GetValue<string>().Should().Be("Withdrawn");
        BookingOf(shipment, secondBookingNumber)["status"]!.GetValue<string>().Should().Be("Requested");
    }

    private static JsonNode BookingOf(JsonNode shipment, string carrierBookingNumber) =>
        shipment["carrierBookings"]!.AsArray().Single(booking => booking!["carrierBookingNumber"]!.GetValue<string>() == carrierBookingNumber)!;

    private static bool Names(ServiceBusReceivedMessage message, string carrierBookingNumber) =>
        message.ApplicationProperties.TryGetValue("carrierBookingNumber", out var number) && Equals(number, carrierBookingNumber);

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
