using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Norvia.Tms.Api.IntegrationTests.CarrierBookings;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.ProofsOfDelivery;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Billing;

/// <summary>What the Billing tests do at the TMS's boundary: carry a Transport Order to a Shipment a Carrier can deliver, and look for what it publishes.</summary>
internal static class BillingCalls
{
    /// <summary>A Transport Order's Shipment, Booked with the Carrier's own booking reference.</summary>
    public sealed record BookedOrder(string TransportOrderNumber, string ShipmentNumber, string CarrierBookingReference);

    /// <summary>Assigns the Transport Order to the test Carrier, releases its Shipment and has the Carrier confirm it.</summary>
    public static async Task<BookedOrder> BookedAsync(this TmsFixture tms, string transportOrderNumber, CancellationToken cancellationToken)
    {
        var assigned = await tms.AssignPlannedAsync(transportOrderNumber, PlanningCalls.ToCarrier(), cancellationToken);
        var shipmentNumber = assigned["shipmentNumber"]!.GetValue<string>();
        await tms.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", cancellationToken);

        var released = await tms.ReleasedAsync(shipmentNumber, cancellationToken);
        await tms.PublishedAsync(released["correlationId"]!.GetValue<string>(), "ShipmentReleased", cancellationToken);

        var reference = CarrierBookingCalls.NewReference();
        await tms.ConfirmedAsync(released["carrierBookingNumber"]!.GetValue<string>(), PlanningCalls.CarrierId, reference, cancellationToken);
        return new(transportOrderNumber, shipmentNumber, reference);
    }

    /// <summary>The Carrier reports the Shipment Delivered, on the correlation ID.</summary>
    public static async Task DeliveredAsync(this TmsFixture tms, BookedOrder order, string correlationId, CancellationToken cancellationToken)
    {
        var delivered = await tms.RecordedAsync(
            TrackingEventCalls.Event("Delivered", order.CarrierBookingReference, DateTimeOffset.UtcNow.AddMinutes(-1)), cancellationToken, correlationId);
        delivered["shipmentStatus"]!.GetValue<string>().Should().Be("Delivered");
    }

    /// <summary>The Carrier sends a clean Proof of delivery for the Shipment, on the correlation ID.</summary>
    public static async Task ProofRecordedAsync(this TmsFixture tms, BookedOrder order, string correlationId, CancellationToken cancellationToken, string? documentId = null) =>
        await tms.RecordedProofAsync(
            ProofOfDeliveryCalls.Metadata(order.CarrierBookingReference, documentId: documentId), [ProofOfDeliveryCalls.Cmr()], cancellationToken, correlationId);

    /// <summary>A Transport Order's billing state, from the query API.</summary>
    public static async Task<string> BillingStateAsync(this TmsFixture tms, string transportOrderNumber, CancellationToken cancellationToken) =>
        (await tms.GetTransportOrderAsync(transportOrderNumber, cancellationToken))["billingState"]!.GetValue<string>();

    /// <summary>The TransportOrderBillable messages published for the Transport Order so far.</summary>
    public static IReadOnlyList<ServiceBusReceivedMessage> BillablePublished(this TmsFixture tms, string transportOrderNumber) =>
        [.. tms.Published.Where(m =>
            m.Subject == "TransportOrderBillable"
            && m.ApplicationProperties.TryGetValue("transportOrderNumber", out var number)
            && Equals(number, transportOrderNumber))];
}
